namespace CamGuard.Api.Common.Data;

public class Camera
{
    public int Id { get; set; }
    public required string Name { get; set; }
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

    public MotionSensitivity MotionSensitivity { get; set; } = MotionSensitivity.Medium;

    /// <summary>La sensibilidad cambió y falta escribirla en la cámara (solo se puede con la cámara despierta).</summary>
    public bool SensitivityPending { get; set; }

    // Último estado leído por DVRIP (ver Features/Devices); null = desconocido o no soportado.
    public int? BatteryPercent { get; set; }
    public long? StorageUsedBytes { get; set; }
    public long? StorageTotalBytes { get; set; }
    public DateTimeOffset? DeviceInfoAt { get; set; }
}

public enum MotionSensitivity
{
    Low,
    Medium,
    High
}

public enum RecordingStatus
{
    Recording,
    Uploading,
    Uploaded,
    Failed
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
}

public enum CameraEventType
{
    Awake,
    Asleep
}

public class CameraEvent
{
    public long Id { get; set; }
    public int CameraId { get; set; }
    public CameraEventType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Fila única (Id = 1) con la configuración editable desde la web.</summary>
public class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public int MaxClipSeconds { get; set; } = 60;
    public int CooldownSeconds { get; set; } = 10;
    public int RetentionDays { get; set; } = 30;
}
