using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Tuya;

public class TuyaOptions
{
    public const string Section = "Tuya";

    /// <summary>OpenAPI del data center del proyecto (Western America: https://openapi.tuyaus.com).</summary>
    public string Endpoint { get; set; } = "https://openapi.tuyaus.com";

    /// <summary>Access ID y Access Secret del proyecto en Tuya Developer Platform. Vacíos = Tuya desactivado.</summary>
    public string AccessId { get; set; } = "";
    public string AccessSecret { get; set; } = "";

    /// <summary>Message Service (Pulsar por WebSocket) del mismo data center.</summary>
    public string MessageEndpoint { get; set; } = "wss://mqe.tuyaus.com:8285/";

    /// <summary>"event" (producción) o "event-test".</summary>
    public string MessageTopic { get; set; } = "event";

    /// <summary>Duración de una activación desde el último evento que graba (no hay aviso de "dormida").</summary>
    public int ActivationSeconds { get; set; } = 90;

    /// <summary>Margen para el primer cuadro: Tuya tarda hasta ~40 s en entregar video si el dispositivo dormía.</summary>
    public int StartupGraceSeconds { get; set; } = 90;

    /// <summary>Cada cuánto se lee el estado (batería) por la API, además de los reportes por mensajes.</summary>
    public int StatusRefreshMinutes { get; set; } = 360;


    /// <summary>Entregar el stream a go2rtc a través de FFmpeg si go2rtc no abre el RTSPS de Tuya.</summary>
    public bool StreamViaFfmpeg { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccessId) && !string.IsNullOrWhiteSpace(AccessSecret);
}

public class TuyaApiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <param name="Uid">Usuario de la app (Smart Life/Tuya) dueño del dispositivo; sirve para listar sus demás dispositivos.</param>
public record TuyaDevice(string Name, string? ProductName, string? Category, bool Online, string? Uid = null);

/// <summary>Un dispositivo de la lista del proyecto (sin <c>local_key</c> ni otros secretos).</summary>
public record TuyaListedDevice(string Id, string Name, string? ProductName, string? Category, bool Online);

/// <param name="NextRowKey">Para pedir la página siguiente; null si no hay más.</param>
public record TuyaDevicePage(IReadOnlyList<TuyaListedDevice> Devices, string? NextRowKey);

/// <summary>
/// Cliente mínimo de la Tuya Cloud API. No registra URLs de stream ni tokens: llevan credenciales temporales.
/// Singleton (guarda el token); el HttpClient sale de la fábrica en cada petición.
/// </summary>
public class TuyaApiClient(IHttpClientFactory httpFactory, IOptions<TuyaOptions> options, TimeProvider clock)
{
    public const string HttpClientName = "tuya";

    /// <summary>"token invalid": el token caducó antes de lo esperado.</summary>
    private const string TokenInvalidCode = "1010";

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires = DateTimeOffset.MinValue;

    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<TuyaDevice> GetDeviceAsync(string deviceId, CancellationToken ct)
    {
        var result = await SendAsync(HttpMethod.Get, $"/v1.0/devices/{Uri.EscapeDataString(deviceId)}", null, ct);
        return new TuyaDevice(
            (string?)result?["name"] ?? deviceId,
            (string?)result?["product_name"],
            (string?)result?["category"],
            (bool?)result?["online"] ?? false,
            (string?)result?["uid"]);
    }

    /// <summary>Tope de páginas al listar, por si la API repite el cursor.</summary>
    public const int MaxListPages = 20;

    /// <summary>
    /// Dispositivos del proyecto: los de las cuentas de la app vinculadas (<c>associated-users</c>). Si ese
    /// endpoint falla y se conoce un <paramref name="fallbackUid"/>, los de ese usuario.
    /// </summary>
    public async Task<IReadOnlyList<TuyaListedDevice>> ListDevicesAsync(string? fallbackUid, CancellationToken ct)
    {
        try
        {
            var devices = new List<TuyaListedDevice>();
            string? rowKey = null;
            for (var page = 0; page < MaxListPages; page++)
            {
                var query = new Dictionary<string, string> { ["size"] = "100" };
                if (rowKey is not null) query["last_row_key"] = Uri.EscapeDataString(rowKey);
                var result = ParseDeviceList(await SendAsync(HttpMethod.Get,
                    TuyaSigner.CanonicalUrl("/v1.0/iot-01/associated-users/devices", query), null, ct));
                devices.AddRange(result.Devices);
                if (result.NextRowKey is null || result.NextRowKey == rowKey) break;
                rowKey = result.NextRowKey;
            }
            return devices;
        }
        catch (TuyaApiException) when (!string.IsNullOrWhiteSpace(fallbackUid))
        {
            return ParseDeviceList(await SendAsync(HttpMethod.Get, $"/v1.0/users/{Uri.EscapeDataString(fallbackUid)}/devices", null, ct)).Devices;
        }
    }

    /// <summary>
    /// Lee <c>result</c> de un listado: un arreglo (<c>/users/{uid}/devices</c>) o un objeto con <c>devices</c>/<c>list</c>,
    /// <c>has_more</c> y <c>last_row_key</c> (<c>associated-users</c>). Se ignoran los elementos sin id.
    /// </summary>
    public static TuyaDevicePage ParseDeviceList(JsonNode? result)
    {
        var items = result switch
        {
            JsonArray array => array,
            JsonObject obj => obj["devices"] as JsonArray ?? obj["list"] as JsonArray,
            _ => null
        };

        var devices = (items ?? [])
            .OfType<JsonObject>()
            .Select(d => (Id: d["id"]?.ToString().Trim(), Device: d))
            .Where(x => !string.IsNullOrEmpty(x.Id))
            .Select(x => new TuyaListedDevice(
                x.Id!,
                (string?)x.Device["name"] is { Length: > 0 } name ? name.Trim() : x.Id!,
                (string?)x.Device["product_name"],
                (string?)x.Device["category"],
                x.Device["online"] is JsonValue online && online.TryGetValue<bool>(out var isOnline) && isOnline))
            .ToList();

        string? next = null;
        if (result is JsonObject page && page["has_more"] is JsonValue more && more.TryGetValue<bool>(out var hasMore) && hasMore)
            next = page["last_row_key"]?.ToString() is { Length: > 0 } key ? key : null;
        return new TuyaDevicePage(devices, next);
    }

    public async Task<IReadOnlyList<TuyaStatus>> GetStatusAsync(string deviceId, CancellationToken ct)
    {
        var result = await SendAsync(HttpMethod.Get, $"/v1.0/devices/{Uri.EscapeDataString(deviceId)}/status", null, ct);
        return result is JsonArray items
            ? items.OfType<JsonObject>()
                .Where(s => (string?)s["code"] is not null)
                .Select(s => new TuyaStatus((string)s["code"]!, s["value"]?.DeepClone(), null))
                .ToList()
            : [];
    }

    /// <summary>URL temporal del video (la pide la API de IPC; despierta el dispositivo).</summary>
    /// <param name="type">"rtsp" (RTSPS) o "hls".</param>
    public async Task<string> AllocateStreamUrlAsync(string deviceId, string type, CancellationToken ct)
    {
        var result = await SendAsync(HttpMethod.Post, $"/v1.0/devices/{Uri.EscapeDataString(deviceId)}/stream/actions/allocate",
            new JsonObject { ["type"] = type }, ct);
        return (string?)result?["url"] ?? throw new TuyaApiException("no_url", "Tuya no devolvió la URL del stream.");
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        try
        {
            return await SendSignedAsync(method, path, body, await GetTokenAsync(ct), ct);
        }
        catch (TuyaApiException ex) when (ex.Code == TokenInvalidCode)
        {
            _token = null;
            return await SendSignedAsync(method, path, body, await GetTokenAsync(ct), ct);
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && clock.GetUtcNow() < _tokenExpires) return _token;

            var result = await SendSignedAsync(HttpMethod.Get, TuyaSigner.CanonicalUrl("/v1.0/token", new Dictionary<string, string> { ["grant_type"] = "1" }), null, "", ct);
            _token = (string?)result?["access_token"] ?? throw new TuyaApiException("no_token", "Tuya no devolvió el token.");
            var expiresIn = (int?)result!["expire_time"] ?? 3600;
            // Se renueva un minuto antes de que caduque.
            _tokenExpires = clock.GetUtcNow().AddSeconds(Math.Max(expiresIn - 60, 30));
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<JsonNode?> SendSignedAsync(HttpMethod method, string url, JsonObject? body, string accessToken, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured) throw new TuyaApiException("not_configured", "Faltan las credenciales de Tuya (TUYA_ACCESS_ID y TUYA_ACCESS_SECRET).");

        var bodyBytes = body is null ? [] : Encoding.UTF8.GetBytes(body.ToJsonString());
        var timestamp = clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var sign = TuyaSigner.Sign(o.AccessId, o.AccessSecret, accessToken, timestamp, nonce,
            TuyaSigner.StringToSign(method.Method, bodyBytes, url));

        using var request = new HttpRequestMessage(method, o.Endpoint.TrimEnd('/') + url);
        request.Headers.Add("client_id", o.AccessId);
        request.Headers.Add("sign", sign);
        request.Headers.Add("t", timestamp);
        request.Headers.Add("nonce", nonce);
        request.Headers.Add("sign_method", "HMAC-SHA256");
        if (accessToken.Length > 0) request.Headers.Add("access_token", accessToken);
        if (body is not null) request.Content = new ByteArrayContent(bodyBytes) { Headers = { { "Content-Type", "application/json" } } };

        using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        if ((bool?)json?["success"] != true)
        {
            var code = json?["code"]?.ToString() ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            throw new TuyaApiException(code, $"Tuya respondió [{code}] {(string?)json?["msg"] ?? response.ReasonPhrase}");
        }

        return json["result"];
    }
}
