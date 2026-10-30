namespace CamGuard.Api.Features.Health;

public class HealthOptions
{
    public const string Section = "Health";

    /// <summary>Cada cuánto se revisa el sistema.</summary>
    public int CheckMinutes { get; set; } = 5;

    /// <summary>Un problema que sigue abierto se vuelve a avisar tras estas horas.</summary>
    public int RepeatHours { get; set; } = 24;

    /// <summary>Tiempo caído antes de avisar de go2rtc, Tuya o R2 (evita avisos por cortes breves).</summary>
    public int ServiceGraceMinutes { get; set; } = 10;

    /// <summary>Un clip esperando subir más que esto se da por atascado.</summary>
    public int UploadStuckMinutes { get; set; } = 30;

    /// <summary>Espacio libre mínimo en el disco de {DataDirectory}.</summary>
    public int MinFreeDiskMb { get; set; } = 1024;

    /// <summary>Días de historial de batería que se conservan (independiente de la retención de clips).</summary>
    public int BatteryHistoryDays { get; set; } = 90;

    // Estimación del costo de R2: los primeros GB son gratis.
    public double R2FreeGb { get; set; } = 10;
    public double R2PricePerGbMonth { get; set; } = 0.015;
}
