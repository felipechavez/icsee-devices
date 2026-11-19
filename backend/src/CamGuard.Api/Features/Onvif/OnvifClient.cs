using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Onvif;

/// <summary>Conexión lógica con una cámara: host, puerto, credenciales y el desfase de su reloj.</summary>
public class OnvifSession(string host, int port, OnvifCredentials credentials)
{
    public string Host { get; } = host;
    public int Port { get; } = port;
    public OnvifCredentials Credentials { get; } = credentials;

    /// <summary>Hora de la cámara menos la del equipo; WS-Security rechaza un Created muy desfasado.</summary>
    public TimeSpan ClockOffset { get; set; }

    public Uri DeviceService => OnvifParser.RebaseHttp(null, Host, Port, "/onvif/device_service");

    public Uri Rebase(string? address, string defaultPath) => OnvifParser.RebaseHttp(address, Host, Port, defaultPath);
}

/// <param name="Uri">URI RTSP sin credenciales.</param>
/// <param name="Codec">"H264", "H265"…; null si no se pudo saber.</param>
public record OnvifStream(string Uri, string? Codec, int? Width, int? Height)
{
    public string Describe() =>
        $"{FormatCodec(Codec)}{(Width is { } w && Height is { } h ? $" {w}×{h}" : "")}";

    private static string FormatCodec(string? codec) => codec switch
    {
        "H264" => "H.264",
        "H265" => "H.265",
        null => "video",
        _ => codec
    };
}

public record OnvifStreams(OnvifStream Main, OnvifStream Sub);

/// <summary>
/// Cliente ONVIF mínimo sobre HttpClient (sin WCF): reloj, capacidades, modelo, perfiles de video y
/// eventos PullPoint. Todas las direcciones se apuntan a la cámara configurada (<see cref="OnvifParser.RebaseHttp"/>).
/// Nunca registra la contraseña: en los mensajes solo viaja el digest.
/// </summary>
public class OnvifClient(IHttpClientFactory httpFactory, TimeProvider clock, IOptions<OnvifOptions> options)
{
    public const string HttpClientName = "onvif";
    private static readonly TimeSpan RtspTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Abre la sesión: mide el desfase del reloj (sin autenticación, como manda ONVIF).</summary>
    public async Task<OnvifSession> ConnectAsync(string host, int port, OnvifCredentials credentials, CancellationToken ct)
    {
        var session = new OnvifSession(host, port, credentials);
        var doc = await SendAsync(session, session.DeviceService, OnvifSoap.Actions.GetSystemDateAndTime,
            OnvifSoap.Bodies.GetSystemDateAndTime, authenticate: false, TimeSpan.Zero, ct);
        session.ClockOffset = OnvifParser.ClockOffset(doc, clock.GetUtcNow()) ?? TimeSpan.Zero;
        return session;
    }

    /// <summary>
    /// ¿Hay un servicio ONVIF en host:puerto? GetSystemDateAndTime sin autenticación (nunca prueba contraseñas:
    /// una Dahua bloquea la cuenta tras varios fallos). Un SOAP Fault también cuenta: habla ONVIF pero exige clave.
    /// </summary>
    public async Task<bool> IdentifyAsync(string host, int port, CancellationToken ct)
    {
        var session = new OnvifSession(host, port, new OnvifCredentials("", ""));
        try
        {
            var doc = await SendAsync(session, session.DeviceService, OnvifSoap.Actions.GetSystemDateAndTime,
                OnvifSoap.Bodies.GetSystemDateAndTime, authenticate: false, TimeSpan.Zero, ct);
            return OnvifParser.IsSystemDateAndTime(doc);
        }
        catch (OnvifException ex)
        {
            return ex.SoapFault;
        }
    }

    public async Task<OnvifCapabilities> GetCapabilitiesAsync(OnvifSession session, CancellationToken ct) =>
        OnvifParser.Capabilities(await SendAsync(session, session.DeviceService, OnvifSoap.Actions.GetCapabilities,
            OnvifSoap.Bodies.GetCapabilities, authenticate: true, TimeSpan.Zero, ct));

    public async Task<OnvifDeviceInfo> GetDeviceInformationAsync(OnvifSession session, CancellationToken ct) =>
        OnvifParser.DeviceInfo(await SendAsync(session, session.DeviceService, OnvifSoap.Actions.GetDeviceInformation,
            OnvifSoap.Bodies.GetDeviceInformation, authenticate: true, TimeSpan.Zero, ct));

    /// <summary>
    /// Perfil principal (mayor resolución, para grabar) y substream (menor, para el vivo), con su URI RTSP.
    /// El códec sale del SDP del propio stream; si el DESCRIBE falla, del perfil ONVIF.
    /// </summary>
    public async Task<OnvifStreams> DiscoverStreamsAsync(OnvifSession session, OnvifCapabilities capabilities, CancellationToken ct)
    {
        var media = session.Rebase(capabilities.MediaXAddr, "/onvif/media_service");
        var profiles = OnvifParser.Profiles(await SendAsync(session, media, OnvifSoap.Actions.GetProfiles,
            OnvifSoap.Bodies.GetProfiles, authenticate: true, TimeSpan.Zero, ct));
        if (OnvifParser.ChooseProfiles(profiles) is not var (main, sub))
            throw new OnvifException("La cámara no informa perfiles de video.");

        var mainStream = await StreamForAsync(session, media, main, ct);
        var subStream = sub.Token == main.Token ? mainStream : await StreamForAsync(session, media, sub, ct);
        return new OnvifStreams(mainStream, subStream);
    }

    public async Task<Uri> CreatePullPointAsync(OnvifSession session, OnvifCapabilities capabilities, TimeSpan termination, CancellationToken ct)
    {
        var events = session.Rebase(capabilities.EventsXAddr, "/onvif/event_service");
        var doc = await SendAsync(session, events, OnvifSoap.Actions.CreatePullPointSubscription,
            OnvifSoap.Bodies.CreatePullPointSubscription(termination), authenticate: true, TimeSpan.Zero, ct);
        // Vacía, relativa o con 0.0.0.0 (pasa en algunas Dahua): RebaseHttp la apunta a la cámara.
        return session.Rebase(OnvifParser.SubscriptionAddress(doc), events.PathAndQuery);
    }

    public async Task<IReadOnlyList<OnvifNotification>> PullMessagesAsync(OnvifSession session, Uri subscription, TimeSpan timeout,
        int limit, CancellationToken ct) =>
        OnvifParser.Notifications(await SendAsync(session, subscription, OnvifSoap.Actions.PullMessages,
            OnvifSoap.Bodies.PullMessages(timeout, limit), authenticate: true, timeout, ct));

    public Task RenewAsync(OnvifSession session, Uri subscription, TimeSpan termination, CancellationToken ct) =>
        SendAsync(session, subscription, OnvifSoap.Actions.Renew, OnvifSoap.Bodies.Renew(termination), authenticate: true, TimeSpan.Zero, ct);

    public Task UnsubscribeAsync(OnvifSession session, Uri subscription, CancellationToken ct) =>
        SendAsync(session, subscription, OnvifSoap.Actions.Unsubscribe, OnvifSoap.Bodies.Unsubscribe, authenticate: true, TimeSpan.Zero, ct);

    private async Task<OnvifStream> StreamForAsync(OnvifSession session, Uri media, OnvifProfile profile, CancellationToken ct)
    {
        var doc = await SendAsync(session, media, OnvifSoap.Actions.GetStreamUri,
            OnvifSoap.Bodies.GetStreamUri(profile.Token), authenticate: true, TimeSpan.Zero, ct);
        var uri = OnvifParser.RebaseRtsp(OnvifParser.StreamUri(doc), session.Host)
                  ?? throw new OnvifException($"La cámara no entregó una URI RTSP para el perfil {profile.Name ?? profile.Token}.");

        string? codec = null;
        try
        {
            var sdp = await RtspDescribe.FetchSdpAsync(uri, session.Credentials.Username, session.Credentials.Password, RtspTimeout, ct);
            codec = RtspDescribe.VideoCodec(sdp);
        }
        catch (Exception ex) when (ex is OnvifException or IOException or System.Net.Sockets.SocketException or OperationCanceledException
                                   && !ct.IsCancellationRequested)
        {
            // Se usa lo que diga el perfil ONVIF.
        }

        return new OnvifStream(uri, codec ?? NormalizeCodec(profile.Encoding), profile.Width, profile.Height);
    }

    public static string? NormalizeCodec(string? encoding) => encoding?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "HEVC" or "H265" or "H.265" => "H265",
        "H264" or "H.264" => "H264",
        var other => other
    };

    /// <summary>Cuerpo HTTP como texto con el charset declarado; si falta o es inválido, UTF-8 (lo que usa ONVIF).</summary>
    public static string DecodeBody(byte[] body, string? charset)
    {
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim().Trim('"'));
            }
            catch (ArgumentException)
            {
            }
        }
        return encoding.GetString(body);
    }

    private async Task<XDocument> SendAsync(OnvifSession session, Uri to, string action, string body, bool authenticate,
        TimeSpan extraTimeout, CancellationToken ct)
    {
        var envelope = OnvifSoap.Envelope(action, to.ToString(), body,
            authenticate ? session.Credentials : null,
            clock.GetUtcNow() + session.ClockOffset,
            RandomNumberGenerator.GetBytes(16));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds) + extraTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, to);
        request.Content = new StringContent(envelope, Encoding.UTF8);
        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
            $"application/soap+xml; charset=utf-8; action=\"{action}\"");

        var http = httpFactory.CreateClient(HttpClientName);
        string text;
        HttpStatusCode status;
        try
        {
            using var response = await http.SendAsync(request, cts.Token);
            status = response.StatusCode;
            // Se decodifica a mano: ReadAsStringAsync lanza si el charset es inválido (hay equipos que mandan "charset=%s").
            text = DecodeBody(await response.Content.ReadAsByteArrayAsync(cts.Token), response.Content.Headers.ContentType?.CharSet);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OnvifException("La cámara no respondió a tiempo.");
        }
        catch (HttpRequestException ex)
        {
            throw new OnvifException($"Sin conexión con la cámara: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            if ((int)status is >= 200 and < 300) return new XDocument();
            throw new OnvifException(status == HttpStatusCode.Unauthorized
                ? "Usuario o contraseña incorrectos (HTTP 401)."
                : $"La cámara respondió HTTP {(int)status}.", status == HttpStatusCode.Unauthorized);
        }

        // Parse lanza OnvifException si es un SOAP Fault (también con HTTP 400/500).
        var doc = OnvifParser.Parse(text);
        if ((int)status is < 200 or >= 300)
            throw new OnvifException($"La cámara respondió HTTP {(int)status}.", status == HttpStatusCode.Unauthorized);
        return doc;
    }
}
