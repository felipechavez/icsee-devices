namespace CamGuard.Api.Features.Discovery;

public class DiscoveryOptions
{
    public const string Section = "Discovery";

    /// <summary>
    /// Subredes a revisar en CIDR, separadas por coma (p. ej. "192.168.1.0/24"). Vacío = los /24 de las
    /// cámaras iCSee/ONVIF ya guardadas. La API está en la red bridge de Docker y no ve la LAN por sí sola.
    /// </summary>
    public string Subnets { get; set; } = "";

    /// <summary>Espera de cada conexión TCP del barrido.</summary>
    public int ConnectTimeoutMs { get; set; } = 400;

    /// <summary>Conexiones TCP simultáneas del barrido.</summary>
    public int MaxConcurrency { get; set; } = 64;

    /// <summary>Tope de equipos por búsqueda (un /22).</summary>
    public int MaxHosts { get; set; } = 1024;

    /// <summary>Tiempo de cada confirmación (login DVRIP o GetSystemDateAndTime de ONVIF).</summary>
    public int ConfirmTimeoutSeconds { get; set; } = 4;

    public int DvripPort { get; set; } = 34567;

    /// <summary>Puertos HTTP donde suele estar el servicio ONVIF, separados por coma.</summary>
    public string OnvifPorts { get; set; } = "80,8000,2020,8080";

    /// <summary>Categorías Tuya con cámara, separadas por coma: sp (cámara/timbre) y videolock (cerradura con video).</summary>
    public string TuyaCameraCategories { get; set; } = "sp,videolock";

    public IReadOnlyList<int> OnvifPortList() =>
        OnvifPorts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, out var port) ? port : 0)
            .Where(p => p is > 0 and <= 65535)
            .Distinct()
            .ToList();

    public IReadOnlySet<string> TuyaCategorySet() =>
        TuyaCameraCategories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
