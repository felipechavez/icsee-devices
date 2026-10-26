using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CamGuard.Api.Features.Tuya;

/// <summary>Un DP reportado por el dispositivo. <paramref name="At"/> es la hora del evento según Tuya.</summary>
public record TuyaStatus(string Code, JsonNode? Value, DateTimeOffset? At);

public record TuyaStatusReport(string DevId, IReadOnlyList<TuyaStatus> Status);

/// <param name="Body">Cuerpo descifrado; null si no se pudo.</param>
/// <param name="Report">Solo para el protocolo 4 (reporte de estado).</param>
/// <param name="Error">Motivo si no se pudo leer.</param>
public record TuyaEnvelope(int? Protocol, string? Em, JsonNode? Body, TuyaStatusReport? Report, string? Error);

/// <summary>
/// Mensajes del Message Service de Tuya (Pulsar por WebSocket). Cada mensaje trae <c>payload</c> en
/// base64 → JSON <c>{ data, protocol, … }</c>; <c>data</c> va cifrado con AES y la clave
/// <c>accessSecret[8..24]</c>: ECB, o GCM si la propiedad <c>em</c> es <c>aes_gcm</c>.
/// </summary>
public static class TuyaMessageDecoder
{
    /// <summary>Protocolo de "reporte de estado de dispositivo".</summary>
    public const int StatusReportProtocol = 4;

    /// <summary>Tipo de mensaje con los DPs reportados en el formato nuevo (reglas del Message Service).</summary>
    public const string PropertyMessageBizCode = "devicePropertyMessage";

    /// <summary>Contraseña del consumidor: md5(accessId + md5(accessSecret))[8..24].</summary>
    public static string MqPassword(string accessId, string accessSecret) =>
        Md5Hex(accessId + Md5Hex(accessSecret)).Substring(8, 16);

    public static string? MessageId(JsonNode message) => (string?)message["messageId"];

    /// <returns>El reporte de DPs, o null si es otro protocolo o no se puede leer.</returns>
    public static TuyaStatusReport? Decode(JsonNode message, string accessSecret) => Open(message, accessSecret).Report;

    /// <summary>
    /// Abre un mensaje y dice qué se encontró, para diagnosticar: protocolo, modo de cifrado,
    /// cuerpo descifrado (sin secretos) o el motivo del fallo.
    /// </summary>
    public static TuyaEnvelope Open(JsonNode message, string accessSecret)
    {
        int? protocol = null;
        var em = (string?)message["properties"]?["em"];
        try
        {
            if ((string?)message["payload"] is not { } payloadBase64) return new(null, em, null, null, "sin payload");
            var payload = JsonNode.Parse(Convert.FromBase64String(payloadBase64));
            protocol = (int?)payload?["protocol"];
            if ((string?)payload?["data"] is not { } data) return new(protocol, em, null, null, "sin data");

            var body = JsonNode.Parse(Decrypt(data, accessSecret, em, payload["t"]?.ToString()));

            // Formato nuevo del Message Service: { bizCode: "devicePropertyMessage", bizData: { devId, properties: [{ code, value, time }] } }
            if ((string?)body?["bizCode"] == PropertyMessageBizCode)
            {
                var bizData = body["bizData"];
                if ((string?)bizData?["devId"] is not { } propertyDevId || bizData["properties"] is not JsonArray properties)
                    return new(protocol, em, body, null, "devicePropertyMessage sin devId o properties");
                return new(protocol, em, body, new TuyaStatusReport(propertyDevId, ReadStatus(properties, "time")), null);
            }

            // Formato clásico: protocolo 4 con { devId, status: [{ code, value, t }] }.
            if (protocol != StatusReportProtocol) return new(protocol, em, body, null, null);
            if ((string?)body?["devId"] is not { } devId || body["status"] is not JsonArray status)
                return new(protocol, em, body, null, "reporte sin devId o status");
            return new(protocol, em, body, new TuyaStatusReport(devId, ReadStatus(status, "t")), null);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or InvalidOperationException or ArgumentException)
        {
            return new(protocol, em, null, null, ex.Message);
        }
    }

    /// <param name="em">Propiedad "em" del mensaje: "aes_gcm" o vacía. Sin ella se prueba ECB y luego GCM.</param>
    /// <param name="aad">Hora del payload; algunos mensajes GCM la usan como dato asociado.</param>
    public static string Decrypt(string base64, string accessSecret, string? em = null, string? aad = null)
    {
        var key = Key(accessSecret);
        var bytes = Convert.FromBase64String(base64);
        if (em != "aes_gcm")
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = key;
                return Encoding.UTF8.GetString(aes.DecryptEcb(bytes, PaddingMode.PKCS7));
            }
            catch (CryptographicException) when (em is null)
            {
                // Sin "em" puede venir igual en GCM.
            }
        }

        try
        {
            return DecryptGcm(bytes, key, null);
        }
        catch (CryptographicException) when (!string.IsNullOrEmpty(aad))
        {
            return DecryptGcm(bytes, key, Encoding.UTF8.GetBytes(aad));
        }
    }

    /// <summary>GCM: IV de 12 bytes al inicio y tag de 16 al final.</summary>
    private static string DecryptGcm(byte[] bytes, byte[] key, byte[]? aad)
    {
        if (bytes.Length < 28) throw new CryptographicException("Mensaje GCM demasiado corto.");
        var nonce = bytes.AsSpan(0, 12);
        var tag = bytes.AsSpan(bytes.Length - 16);
        var cipher = bytes.AsSpan(12, bytes.Length - 28);
        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, 16);
        gcm.Decrypt(nonce, cipher, tag, plain, aad);
        return Encoding.UTF8.GetString(plain);
    }

    public static byte[] Key(string accessSecret) => Encoding.UTF8.GetBytes(accessSecret.Substring(8, 16));

    private static List<TuyaStatus> ReadStatus(JsonArray items, string timeField) => items
        .OfType<JsonObject>()
        .Where(s => (string?)s["code"] is not null)
        .Select(s => new TuyaStatus((string)s["code"]!, s["value"]?.DeepClone(), ParseTime(s[timeField] ?? s["t"])))
        .ToList();

    /// <summary>Tuya envía la hora en milisegundos o en segundos según el mensaje.</summary>
    private static DateTimeOffset? ParseTime(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<long>(out var t) || t <= 0) return null;
        return t < 10_000_000_000 ? DateTimeOffset.FromUnixTimeSeconds(t) : DateTimeOffset.FromUnixTimeMilliseconds(t);
    }

    private static string Md5Hex(string s) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(s)));
}
