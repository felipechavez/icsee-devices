namespace CamGuard.Api.Common.Data;

public class Camera
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>iCSee (DVRIP en la LAN) o Tuya (cámara, timbre o cerradura; todo por la nube). No cambia después del alta.</summary>
    public CameraKind Kind { get; set; } = CameraKind.Icsee;

    /// <summary>IP de una iCSee; vacía en las Tuya.</summary>
    public required string Ip { get; set; }
    public int DvripPort { get; set; } = 34567;
    public required string Go2rtcStream { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Usuario DVRIP de la cámara.</summary>
    public string Username { get; set; } = "";

    /// <summary>Contraseña DVRIP cifrada (ver CameraSecretProtector). Null = sin configurar.</summary>
    public string? PasswordProtected { get; set; }

    /// <summary>Modelo editable; si está vacío se completa con el que informa la cámara.</summary>
    public string? Model { get; set; }

    /// <summary>Device ID en la Tuya Cloud API; solo en las Tuya.</summary>
    public string? TuyaDeviceId { get; set; }

    /// <summary>
    /// Device IDs de sensores Tuya (p. ej. PIR) separados por coma: su movimiento despierta y graba esta
    /// cámara. Para cámaras sin detección propia, como una cerradura con video.
    /// </summary>
    public string? TuyaTriggerDeviceIds { get; set; }

    /// <summary>Separa una lista de Device IDs escrita con comas, espacios o saltos de línea.</summary>
    public static IReadOnlyList<string> SplitDeviceIds(string? ids) =>
        string.IsNullOrWhiteSpace(ids)
            ? []
            : ids.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Giro a la derecha en grados (0, 90, 180, 270) para cámaras montadas de lado.</summary>
    public int Rotation { get; set; }

    public MotionSensitivity MotionSensitivity { get; set; } = MotionSensitivity.Medium;

    /// <summary>Qué hace la IA con los clips de esta cámara (ver Features/Detection).</summary>
    public DetectionMode DetectionMode { get; set; } = DetectionMode.Tag;

    public bool DetectPeople { get; set; } = true;
    public bool DetectAnimals { get; set; } = true;

    /// <summary>La sensibilidad cambió y falta escribirla en la cámara (solo se puede con la cámara despierta).</summary>
    public bool SensitivityPending { get; set; }

    // Último estado leído por DVRIP (ver Features/Devices); null = desconocido o no soportado.
    public int? BatteryPercent { get; set; }
    public long? StorageUsedBytes { get; set; }
    public long? StorageTotalBytes { get; set; }
    public DateTimeOffset? DeviceInfoAt { get; set; }
}

public enum CameraKind
{
    Icsee,
    Tuya
}

public enum MotionSensitivity
{
    Low,
    Medium,
    High
}

public enum DetectionMode
{
    /// <summary>Sin análisis: se sube todo.</summary>
    Off,

    /// <summary>Analiza y etiqueta, pero sube todo (para calibrar).</summary>
    Tag,

    /// <summary>Descarta los clips sin personas ni animales.</summary>
    Filter
}

public enum RecordingStatus
{
    Recording,

    /// <summary>Grabado; esperando a la IA (Features/Detection) antes de subirse.</summary>
    Analyzing,
    Uploading,
    Uploaded,
    Failed,

    /// <summary>La IA no encontró personas ni animales (modo Filter): no se sube y los archivos se borran.</summary>
    Discarded
}

public class Recording
{
    public Guid Id { get; set; }
    public int CameraId { get; set; }
    public Camera? Camera { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public double? DurationSec { get; set; }
    public long? SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public string? ThumbnailKey { get; set; }
    public RecordingStatus Status { get; set; }

    /// <summary>Lo que vio la IA: "persona", "perro"… o "nada". Null = sin analizar (modo Off o el análisis falló).</summary>
    public string? Detection { get; set; }

    /// <summary>Confianza de <see cref="Detection"/> (0–1); con "nada", la mayor por debajo del umbral.</summary>
    public double? DetectionScore { get; set; }
}

public enum CameraEventType
{
    Awake,
    Asleep,

    // Dispositivos Tuya
    Motion,
    Doorbell,
    Alarm,
    Unlock,

    /// <summary>Evento con foto de una cerradura aún sin tipo identificado.</summary>
    LockEvent
}

public class CameraEvent
{
    public long Id { get; set; }
    public int CameraId { get; set; }
    public CameraEventType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Detalle del evento de un dispositivo Tuya, p. ej. "wrong_password" o "fingerprint #1".</summary>
    public string? Detail { get; set; }
}

/// <summary>Fila única (Id = 1) con la configuración editable desde la web.</summary>
public class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public int MaxClipSeconds { get; set; } = 60;
    public int CooldownSeconds { get; set; } = 10;
    public int RetentionDays { get; set; } = 30;

    /// <summary>Confianza mínima (0–1) para dar por vista una persona o un animal.</summary>
    public double DetectionMinConfidence { get; set; } = 0.5;
}
