using System.Security.Cryptography;
using System.Text;

namespace CamGuard.Api.Features.Tuya;

/// <summary>
/// Firma de la Tuya Cloud API (HMAC-SHA256). La misma que usa poc/tuya/tuya-api.ps1.
/// </summary>
public static class TuyaSigner
{
    /// <summary>Ruta con la query ordenada por clave: así la espera la firma.</summary>
    public static string CanonicalUrl(string path, IReadOnlyDictionary<string, string>? query = null)
    {
        if (query is null || query.Count == 0) return path;
        var pairs = query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => $"{q.Key}={q.Value}");
        return $"{path}?{string.Join('&', pairs)}";
    }

    public static string StringToSign(string method, ReadOnlySpan<byte> body, string canonicalUrl) =>
        $"{method.ToUpperInvariant()}\n{Convert.ToHexStringLower(SHA256.HashData(body))}\n\n{canonicalUrl}";

    /// <param name="accessToken">Vacío al pedir el token.</param>
    public static string Sign(string clientId, string secret, string accessToken, string timestamp, string nonce, string stringToSign)
    {
        var message = Encoding.UTF8.GetBytes(clientId + accessToken + timestamp + nonce + stringToSign);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message));
    }
}
