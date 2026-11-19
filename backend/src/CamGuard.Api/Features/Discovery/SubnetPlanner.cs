using System.Net;
using System.Net.Sockets;

namespace CamGuard.Api.Features.Discovery;

/// <param name="Subnets">Subredes que se revisan, en CIDR.</param>
/// <param name="Hosts">Direcciones a sondear, sin repetir y sin la de red ni la de broadcast.</param>
/// <param name="Warnings">Lo que se descartó y por qué (se muestra en la web).</param>
public record SubnetPlan(IReadOnlyList<string> Subnets, IReadOnlyList<string> Hosts, IReadOnlyList<string> Warnings);

/// <summary>Qué direcciones revisar al buscar cámaras. Lógica pura.</summary>
public static class SubnetPlanner
{
    /// <param name="configured">CIDR separados por coma (<see cref="DiscoveryOptions.Subnets"/>); vacío = derivar de las cámaras.</param>
    /// <param name="cameraIps">IPs de las cámaras iCSee/ONVIF guardadas: sus /24 se usan si no hay subredes configuradas.</param>
    public static SubnetPlan Plan(string? configured, IEnumerable<string> cameraIps, int maxHosts)
    {
        var warnings = new List<string>();
        var candidates = new List<(uint Network, int Prefix)>();

        if (!string.IsNullOrWhiteSpace(configured))
        {
            foreach (var text in configured.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseCidr(text, out var network, out var prefix))
                    candidates.Add((network, prefix));
                else
                    warnings.Add($"Subred inválida: {text} (usa el formato 192.168.1.0/24).");
            }
        }
        else
        {
            foreach (var ip in cameraIps)
            {
                if (TryParseIpv4(ip.Trim(), out var value))
                    candidates.Add((value & Mask(24), 24));
            }
            if (candidates.Count == 0)
                warnings.Add("No hay subred para revisar: configura LAN_SUBNETS (p. ej. 192.168.1.0/24) en .env.");
        }

        var subnets = new List<string>();
        var hosts = new List<string>();
        var seen = new HashSet<uint>();
        foreach (var (network, prefix) in candidates.Distinct())
        {
            var cidr = $"{Format(network)}/{prefix}";
            if (!IsPrivate(network))
            {
                warnings.Add($"{cidr} no es una red local privada: se omite.");
                continue;
            }

            var (first, last) = HostRange(network, prefix);
            var count = (long)last - first + 1;
            if (hosts.Count + count > maxHosts)
            {
                warnings.Add($"{cidr} tiene demasiadas direcciones (máx. {maxHosts} por búsqueda): se omite.");
                continue;
            }

            subnets.Add(cidr);
            // Las redes privadas terminan antes de 255.255.255.255: el contador no se desborda.
            for (var a = first; a <= last; a++)
            {
                if (seen.Add(a)) hosts.Add(Format(a));
            }
        }

        return new SubnetPlan(subnets, hosts, warnings);
    }

    /// <summary>"192.168.1.37/24" → red 192.168.1.0, prefijo 24. Una IP sola vale como /32.</summary>
    public static bool TryParseCidr(string text, out uint network, out int prefix)
    {
        network = 0;
        prefix = 32;
        var parts = text.Trim().Split('/');
        if (parts.Length > 2 || !TryParseIpv4(parts[0], out var address)) return false;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix is < 8 or > 32)) return false;
        network = address & Mask(prefix);
        return true;
    }

    /// <summary>Hosts utilizables: en /31 y /32 todos; en el resto, sin la de red ni la de broadcast.</summary>
    public static (uint First, uint Last) HostRange(uint network, int prefix)
    {
        var broadcast = network | ~Mask(prefix);
        return prefix >= 31 ? (network, broadcast) : (network + 1, broadcast - 1);
    }

    /// <summary>10/8, 172.16/12 y 192.168/16 (RFC 1918).</summary>
    public static bool IsPrivate(uint address) =>
        (address & Mask(8)) == 0x0A000000
        || (address & Mask(12)) == 0xAC100000
        || (address & Mask(16)) == 0xC0A80000;

    private static uint Mask(int prefix) => prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);

    private static bool TryParseIpv4(string text, out uint value)
    {
        value = 0;
        // IPAddress.TryParse acepta "1" o "1.2" como IPv4 abreviadas: se exigen los cuatro octetos.
        if (text.Count(c => c == '.') != 3 || !IPAddress.TryParse(text, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var bytes = ip.GetAddressBytes();
        value = (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
        return true;
    }

    private static string Format(uint value) => $"{value >> 24}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
}
