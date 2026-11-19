using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Xml;
using System.Xml.Linq;

namespace CamGuard.Api.Features.Onvif;

/// <summary>Error de una cámara ONVIF: un SOAP Fault, una respuesta ilegible o un dato que falta.</summary>
public class OnvifException(string message, bool unauthorized = false, bool soapFault = false) : Exception(message)
{
    /// <summary>La cámara rechazó usuario o contraseña. Varios intentos seguidos pueden bloquear la cuenta.</summary>
    public bool Unauthorized { get; } = unauthorized;

    /// <summary>La respuesta fue un SOAP Fault: el equipo habla SOAP aunque rechace la petición.</summary>
    public bool SoapFault { get; } = soapFault;
}

/// <param name="MediaXAddr">Servicio Media (perfiles y URIs de video).</param>
/// <param name="EventsXAddr">Servicio Events (suscripción PullPoint).</param>
public record OnvifCapabilities(string? MediaXAddr, string? EventsXAddr, bool PullPointSupport);

public record OnvifDeviceInfo(string? Manufacturer, string? Model, string? Firmware)
{
    /// <summary>"Imou IPC-C22EP" o lo que haya; null si la cámara no informa nada.</summary>
    public string? DisplayModel =>
        string.Join(' ', new[] { Manufacturer, Model }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())) is { Length: > 0 } text
            ? text
            : null;
}

public record OnvifProfile(string Token, string? Name, string? Encoding, int? Width, int? Height)
{
    public long Pixels => (long)(Width ?? 0) * (Height ?? 0);
}

/// <param name="Topic">Tópico sin prefijos de namespace, p. ej. "VideoSource/MotionAlarm".</param>
/// <param name="Operation">"Initialized" (estado al suscribirse), "Changed" o "Deleted".</param>
/// <param name="Data">SimpleItem de Data (p. ej. State=true).</param>
public record OnvifNotification(
    string Topic,
    string? Operation,
    DateTimeOffset? UtcTime,
    IReadOnlyDictionary<string, string> Source,
    IReadOnlyDictionary<string, string> Data);

/// <summary>Lee las respuestas SOAP de ONVIF por nombre local (cada fabricante usa sus prefijos).</summary>
public static class OnvifParser
{
    private static readonly XmlReaderSettings SafeXml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 4 * 1024 * 1024
    };

    /// <summary>Parsea la respuesta y lanza <see cref="OnvifException"/> si es un SOAP Fault.</summary>
    public static XDocument Parse(string xml)
    {
        XDocument doc;
        try
        {
            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, SafeXml);
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new OnvifException($"Respuesta ONVIF ilegible: {ex.Message}");
        }

        if (Descendant(doc, "Fault") is { } fault)
        {
            var reason = Descendant(fault, "Text")?.Value.Trim() ?? Descendant(fault, "faultstring")?.Value.Trim();
            var codes = Descendants(fault, "Value").Select(v => v.Value).ToList();
            var unauthorized = codes.Any(c => c.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase))
                               || reason?.Contains("not authorized", StringComparison.OrdinalIgnoreCase) == true
                               || reason?.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) == true;
            var detail = string.IsNullOrEmpty(reason) ? codes.LastOrDefault() ?? "error sin detalle" : reason;
            throw new OnvifException(unauthorized ? $"Usuario o contraseña incorrectos ({detail})" : $"La cámara respondió un error: {detail}",
                unauthorized, soapFault: true);
        }

        return doc;
    }

    /// <summary>¿Es la respuesta de GetSystemDateAndTime? Distingue una ONVIF de cualquier servidor HTTP.</summary>
    public static bool IsSystemDateAndTime(XDocument doc) => Descendant(doc, "GetSystemDateAndTimeResponse") is not null;

    /// <summary>Hora de la cámara menos la del equipo; null si no informa la hora UTC.</summary>
    public static TimeSpan? ClockOffset(XDocument doc, DateTimeOffset now)
    {
        if (Descendant(doc, "UTCDateTime") is not { } utc) return null;
        var date = Child(utc, "Date");
        var time = Child(utc, "Time");
        if (date is null || time is null) return null;

        try
        {
            var camera = new DateTimeOffset(
                Int(date, "Year"), Int(date, "Month"), Int(date, "Day"),
                Int(time, "Hour"), Int(time, "Minute"), Int(time, "Second"), TimeSpan.Zero);
            return camera - now;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static OnvifCapabilities Capabilities(XDocument doc)
    {
        var media = XAddrOf(doc, "Media");
        var events = Descendants(doc, "Events").FirstOrDefault(e => Child(e, "XAddr") is not null);
        var pullPoint = events is not null && Child(events, "WSPullPointSupport")?.Value.Trim() is { } v
                        && v.Equals("true", StringComparison.OrdinalIgnoreCase);
        return new OnvifCapabilities(media, events is null ? null : Child(events, "XAddr")!.Value.Trim(), pullPoint);
    }

    public static OnvifDeviceInfo DeviceInfo(XDocument doc) =>
        new(Text(doc, "Manufacturer"), Text(doc, "Model"), Text(doc, "FirmwareVersion"));

    public static IReadOnlyList<OnvifProfile> Profiles(XDocument doc) =>
        Descendants(doc, "Profiles")
            .Select(p =>
            {
                var encoder = Descendant(p, "VideoEncoderConfiguration");
                var resolution = encoder is null ? null : Descendant(encoder, "Resolution");
                return new OnvifProfile(
                    (string?)p.Attribute("token") ?? "",
                    Child(p, "Name")?.Value.Trim(),
                    encoder is null ? null : Child(encoder, "Encoding")?.Value.Trim(),
                    resolution is null ? null : TryInt(resolution, "Width"),
                    resolution is null ? null : TryInt(resolution, "Height"));
            })
            .Where(p => p.Token.Length > 0)
            .ToList();

    /// <summary>
    /// Principal = el perfil de mayor resolución (graba); substream = el de menor (vivo). Con un solo
    /// perfil, ambos son el mismo. Sin resolución informada se respeta el orden de la cámara.
    /// </summary>
    public static (OnvifProfile Main, OnvifProfile Sub)? ChooseProfiles(IReadOnlyList<OnvifProfile> profiles)
    {
        if (profiles.Count == 0) return null;
        var ordered = profiles.Select((p, i) => (p, i)).OrderByDescending(x => x.p.Pixels).ThenBy(x => x.i).Select(x => x.p).ToList();
        return (ordered[0], ordered[^1]);
    }

    public static string? StreamUri(XDocument doc) => Text(doc, "Uri");

    /// <summary>Dirección de la suscripción PullPoint, tal como la informa la cámara (puede venir vacía).</summary>
    public static string? SubscriptionAddress(XDocument doc) =>
        Descendant(doc, "SubscriptionReference") is { } reference ? Descendant(reference, "Address")?.Value.Trim() : null;

    public static IReadOnlyList<OnvifNotification> Notifications(XDocument doc) =>
        Descendants(doc, "NotificationMessage")
            .Select(m =>
            {
                var topic = NormalizeTopic(Descendant(m, "Topic")?.Value ?? "");
                // La carga útil es Message/Message (wsnt:Message envuelve a tt:Message).
                var payload = Descendants(m, "Message").FirstOrDefault(e => e.Attribute("UtcTime") is not null)
                              ?? Descendants(m, "Message").LastOrDefault();
                DateTimeOffset? at = null;
                if ((string?)payload?.Attribute("UtcTime") is { } raw
                    && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                    at = parsed;

                return new OnvifNotification(
                    topic,
                    (string?)payload?.Attribute("PropertyOperation"),
                    at,
                    Items(payload, "Source"),
                    Items(payload, "Data"));
            })
            .Where(n => n.Topic.Length > 0)
            .ToList();

    /// <summary>"tns1:VideoSource/tnsdahua:MotionAlarm" → "VideoSource/MotionAlarm".</summary>
    public static string NormalizeTopic(string topic) =>
        string.Join('/', topic.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment[(segment.LastIndexOf(':') + 1)..]));

    /// <summary>
    /// Dirección HTTP de un servicio ONVIF apuntada siempre a la cámara configurada: se conservan ruta y query,
    /// pero host y puerto son los de la cámara. Así una XAddr vacía, relativa, con 0.0.0.0 o con otra IP de la
    /// cámara (las Dahua responden también en 192.168.1.108) funciona, y nunca se contacta otro equipo.
    /// </summary>
    public static Uri RebaseHttp(string? address, string host, int port, string defaultPath)
    {
        var path = defaultPath;
        if (!string.IsNullOrWhiteSpace(address))
        {
            var trimmed = address.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
                path = absolute.PathAndQuery;
            else if (trimmed.StartsWith('/'))
                path = trimmed;
        }

        return new Uri($"http://{HostForUri(host)}:{port.ToString(CultureInfo.InvariantCulture)}{path}");
    }

    /// <summary>URI RTSP con el host de la cámara configurada; conserva puerto (554 si no trae), ruta y query.</summary>
    public static string? RebaseRtsp(string? uri, string host)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var parsed)
            || !parsed.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
            return null;
        var port = parsed.IsDefaultPort || parsed.Port <= 0 ? 554 : parsed.Port;
        // Sin credenciales: la URI se guarda en BD y la contraseña se agrega solo al registrar el stream.
        return $"rtsp://{HostForUri(host)}:{port.ToString(CultureInfo.InvariantCulture)}{parsed.PathAndQuery}";
    }

    public static string HostForUri(string host) =>
        IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{host}]" : host;

    private static IReadOnlyDictionary<string, string> Items(XElement? payload, string section)
    {
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        if (payload is null || Child(payload, section) is not { } container) return items;
        foreach (var item in Descendants(container, "SimpleItem"))
        {
            if ((string?)item.Attribute("Name") is { Length: > 0 } name)
                items[name] = (string?)item.Attribute("Value") ?? "";
        }
        return items;
    }

    private static string? XAddrOf(XDocument doc, string service) =>
        Descendants(doc, service).Select(e => Child(e, "XAddr")?.Value.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    private static string? Text(XContainer root, string name) =>
        Descendant(root, name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static int Int(XElement parent, string name) =>
        int.Parse(Child(parent, name)?.Value.Trim() ?? throw new FormatException(name), CultureInfo.InvariantCulture);

    private static int? TryInt(XElement parent, string name) =>
        int.TryParse(Child(parent, name)?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static XElement? Child(XElement parent, string name) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static XElement? Descendant(XContainer root, string name) => Descendants(root, name).FirstOrDefault();

    private static IEnumerable<XElement> Descendants(XContainer root, string name) => root.Descendants().Where(e => e.Name.LocalName == name);
}
