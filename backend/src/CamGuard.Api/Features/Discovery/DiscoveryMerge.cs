using System.Net;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Tuya;

namespace CamGuard.Api.Features.Discovery;

public enum LanProtocol
{
    Dvrip,
    Onvif
}

/// <summary>Un puerto de la LAN que respondió con el protocolo esperado.</summary>
/// <param name="Model">Solo DVRIP, si la cámara no tiene contraseña y se pudo leer.</param>
public record LanHit(string Ip, LanProtocol Protocol, int Port, string? Model = null);

/// <summary>Cámara encontrada que aún no está en CamGuard: con esto la web precarga el alta.</summary>
/// <param name="Port">Puerto DVRIP (iCSee) o HTTP del servicio ONVIF; null en Tuya.</param>
public record DiscoveredCamera(
    CameraKind Kind,
    string? Ip,
    int? Port,
    string? TuyaDeviceId,
    string SuggestedName,
    string? Model,
    string? Detail);

/// <summary>Junta lo hallado en la LAN y en Tuya y quita lo que ya está registrado. Lógica pura.</summary>
public static class DiscoveryMerge
{
    public const int MaxNameLength = 100;

    /// <summary>
    /// IP de fábrica de Dahua/Imou: la cámara la sigue contestando como alias además de la que le dio el router,
    /// así que una sola cámara aparece en dos IPs.
    /// </summary>
    public const string DahuaFactoryIp = "192.168.1.108";

    /// <param name="warnings">Aquí se explica lo que se omitió a propósito (p. ej. el alias de fábrica de Dahua).</param>
    public static IReadOnlyList<DiscoveredCamera> Merge(
        IEnumerable<LanHit> hits,
        IEnumerable<TuyaListedDevice> tuyaDevices,
        IEnumerable<Camera> known,
        IReadOnlySet<string> tuyaCameraCategories,
        ICollection<string>? warnings = null)
    {
        var hitList = hits.ToList();
        var cameras = known.ToList();
        // Una IP ya registrada no se ofrece con ningún tipo: una iCSee que además expone ONVIF no se duplica.
        var knownIps = cameras.Where(c => c.Kind != CameraKind.Tuya && !string.IsNullOrWhiteSpace(c.Ip))
            .Select(c => c.Ip.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var knownTuya = cameras.SelectMany(c => Camera.SplitDeviceIds(c.TuyaTriggerDeviceIds).Append(c.TuyaDeviceId ?? ""))
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        // Sin contraseña no se puede leer el número de serie para saber si dos IPs son el mismo equipo. Regla práctica:
        // si 192.168.1.108 responde ONVIF y hay otra ONVIF (registrada o hallada ahora), es el alias de esa cámara.
        // Una Dahua nueva sin DHCP, sola en la red, sí se ofrece en .108.
        var otherOnvif = cameras.Any(c => c.Kind == CameraKind.Onvif && c.Ip.Trim() != DahuaFactoryIp)
                         || hitList.Any(h => h.Protocol == LanProtocol.Onvif && h.Ip != DahuaFactoryIp && !knownIps.Contains(h.Ip));
        var skipFactoryAlias = otherOnvif && !knownIps.Contains(DahuaFactoryIp)
                               && hitList.Any(h => h.Ip == DahuaFactoryIp && h.Protocol == LanProtocol.Onvif)
                               && hitList.All(h => h.Ip != DahuaFactoryIp || h.Protocol == LanProtocol.Onvif);
        if (skipFactoryAlias)
            warnings?.Add($"{DahuaFactoryIp} responde ONVIF: es la IP de fábrica de Dahua/Imou, que la cámara mantiene además de la " +
                          "del router. Se omite porque es la misma cámara que otra ONVIF; agrégala solo con la IP del router.");

        var found = new List<DiscoveredCamera>();
        foreach (var group in hitList.Where(h => !knownIps.Contains(h.Ip) && !(skipFactoryAlias && h.Ip == DahuaFactoryIp)).GroupBy(h => h.Ip))
        {
            var dvrip = group.FirstOrDefault(h => h.Protocol == LanProtocol.Dvrip);
            var onvifPorts = group.Where(h => h.Protocol == LanProtocol.Onvif).Select(h => h.Port).Distinct()
                .OrderBy(p => p == 80 ? 0 : 1).ThenBy(p => p).ToList();

            if (dvrip is not null)
            {
                // DVRIP gana: graba y lee batería/SD; su ONVIF (si lo tiene) es secundario.
                var detail = onvifPorts.Count > 0 ? $"También responde ONVIF (puerto {onvifPorts[0]})." : null;
                found.Add(new DiscoveredCamera(CameraKind.Icsee, group.Key, dvrip.Port, null,
                    $"iCSee {group.Key}", dvrip.Model, detail));
            }
            else if (onvifPorts.Count > 0)
            {
                found.Add(new DiscoveredCamera(CameraKind.Onvif, group.Key, onvifPorts[0], null,
                    $"ONVIF {group.Key}", null, null));
            }
        }

        foreach (var device in tuyaDevices.DistinctBy(d => d.Id))
        {
            if (knownTuya.Contains(device.Id) || device.Category is null || !tuyaCameraCategories.Contains(device.Category))
                continue;
            var name = device.Name.Trim();
            found.Add(new DiscoveredCamera(CameraKind.Tuya, null, null, device.Id,
                name.Length > MaxNameLength ? name[..MaxNameLength] : name,
                device.ProductName, device.Online ? "En línea" : "Fuera de línea"));
        }

        return found
            .OrderBy(c => KindOrder(c.Kind))
            .ThenBy(c => IpSortKey(c.Ip))
            .ThenBy(c => c.SuggestedName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static int KindOrder(CameraKind kind) => kind switch
    {
        CameraKind.Icsee => 0,
        CameraKind.Onvif => 1,
        _ => 2
    };

    /// <summary>192.168.1.9 antes que 192.168.1.10.</summary>
    private static long IpSortKey(string? ip) =>
        ip is not null && IPAddress.TryParse(ip, out var address) && address.GetAddressBytes() is { Length: 4 } b
            ? (long)b[0] << 24 | (long)b[1] << 16 | (long)b[2] << 8 | b[3]
            : long.MaxValue;
}
