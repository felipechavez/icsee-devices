using System.Net;
using System.Net.Sockets;

namespace CamGuard.Api.Features.Streams;

public static class Go2rtcStreamSource
{
    /// <summary>Origen DVRIP que go2rtc usa para abrir la cámara (calidad principal, canal 0).</summary>
    public static string For(string ip, int port, string username, string password)
    {
        var host = IPAddress.TryParse(ip, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{ip}]"
            : ip;
        return $"dvrip://{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@{host}:{port}?channel=0&subtype=0";
    }
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
