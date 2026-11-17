using System.Globalization;
using System.Net;
using System.Net.Sockets;
using CamGuard.Api.Common.Data;

namespace CamGuard.Api.Features.Streams;

public static class Go2rtcStreamSource
{
    /// <summary>Origen DVRIP que go2rtc usa para abrir la cámara (calidad principal, canal 0).</summary>
    public static string For(string ip, int port, string username, string password) =>
        $"dvrip://{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@{Host(ip)}:{port}?channel=0&subtype=0";

    /// <summary>
    /// Origen RTSP de una cámara ONVIF: la URI descubierta con las credenciales escapadas y el host forzado a la IP
    /// configurada (go2rtc nunca abre otro equipo). Null si la URI no es RTSP. Lleva la contraseña: no registrar.
    /// </summary>
    public static string? ForRtsp(string? uri, string ip, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || !parsed.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
            return null;
        var port = parsed.IsDefaultPort || parsed.Port <= 0 ? 554 : parsed.Port;
        return $"rtsp://{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@{Host(ip)}:{port.ToString(CultureInfo.InvariantCulture)}{parsed.PathAndQuery}";
    }

    private static string Host(string ip) =>
        IPAddress.TryParse(ip, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{ip}]" : ip;
}

/// <summary>Nombres de los streams de una cámara en go2rtc.</summary>
public static class Go2rtcStreamNames
{
    /// <summary>Sufijo del stream de vivo de una ONVIF (substream H.264, que el navegador reproduce por WebRTC).</summary>
    public const string LiveSuffix = "_sub";

    /// <summary>Stream para el vivo: el substream en las ONVIF; en las demás, el mismo que se graba.</summary>
    public static string Live(Camera camera) =>
        camera.Kind == CameraKind.Onvif ? camera.Go2rtcStream + LiveSuffix : camera.Go2rtcStream;
}

public record StreamSyncPlan(IReadOnlyList<KeyValuePair<string, string>> ToPatch, IReadOnlyList<string> ToDelete)
{
    /// <summary>
    /// Qué streams registrar o quitar en go2rtc.
    /// </summary>
    /// <param name="desired">Stream → origen de cada cámara habilitada.</param>
    /// <param name="current">Streams que go2rtc tiene ahora, con su origen si lo informa (solo lo hace sin conexión activa).</param>
    /// <param name="applied">Último origen que esta instancia registró en cada stream.</param>
    /// <param name="managedElsewhere">Streams de cámaras cuyo origen se registra al usarlos (dispositivos Tuya): no se tocan.</param>
    public static StreamSyncPlan Compute(
        IReadOnlyDictionary<string, string> desired,
        IReadOnlyDictionary<string, string?> current,
        IReadOnlyDictionary<string, string> applied,
        IReadOnlySet<string>? managedElsewhere = null)
    {
        var toPatch = desired
            .Where(d =>
            {
                if (!current.TryGetValue(d.Key, out var reported)) return true;
                var known = applied.TryGetValue(d.Key, out var last) ? last : reported;
                return known != d.Value;
            })
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .ToList();

        var toDelete = current.Keys
            .Where(name => !desired.ContainsKey(name) && managedElsewhere?.Contains(name) != true)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new StreamSyncPlan(toPatch, toDelete);
    }
}
