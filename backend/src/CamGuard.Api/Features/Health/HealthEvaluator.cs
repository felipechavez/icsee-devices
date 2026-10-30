using System.Globalization;
using CamGuard.Api.Common.Data;

namespace CamGuard.Api.Features.Health;

public enum HealthSeverity
{
    Warning,
    Critical
}

/// <param name="Key">Clave estable: "battery:{id}", "silent:{id}", "service:{nombre}", "queue" o "disk".</param>
/// <param name="Title">Sin números que cambien entre revisiones: se guarda para el "Resuelto: …".</param>
public record HealthIssue(string Key, HealthSeverity Severity, string Title, string? Detail, int? CameraId, DateTimeOffset Since);

public record CameraHealth(
    int Id,
    string Name,
    CameraKind Kind,
    bool Enabled,
    int? BatteryPercent,
    DateTimeOffset? DeviceInfoAt,
    double? BatteryDaysLeft,
    IReadOnlyList<BatteryPoint> BatteryHistory,
    DateTimeOffset? LastAwakeAt,
    double ActivationsPerDay);

/// <param name="Since">Desde cuándo está en el estado actual; en R2 sano, la última subida.</param>
public record ServiceHealth(string Name, bool Ok, DateTimeOffset? Since, string? Detail);

public record UploadsHealth(int Pending, int Analyzing, DateTimeOffset? OldestPendingAt);

public record DiskHealth(long FreeBytes, long TotalBytes);

public record StorageUsage(long UsedBytes, int Clips, double EstimatedMonthlyUsd);

/// <param name="MonitorStartedAt">Para una cámara que nunca despertó, la cuenta de "sin despertar" parte de aquí.</param>
public record HealthFacts(
    IReadOnlyList<CameraHealth> Cameras,
    IReadOnlyList<ServiceHealth> Services,
    UploadsHealth Uploads,
    DiskHealth? Disk,
    StorageUsage Storage,
    DateTimeOffset MonitorStartedAt);

public record HealthThresholds(int LowBatteryPercent, int SilentCameraHours, int UploadStuckMinutes, int MinFreeDiskMb, int ServiceGraceMinutes);

public record SystemStatus(
    DateTimeOffset CheckedAt,
    IReadOnlyList<HealthIssue> Issues,
    IReadOnlyList<CameraHealth> Cameras,
    IReadOnlyList<ServiceHealth> Services,
    UploadsHealth Uploads,
    DiskHealth? Disk,
    StorageUsage Storage);

/// <summary>Convierte el estado reunido en la lista de problemas abiertos. Nunca despierta una cámara.</summary>
public static class HealthEvaluator
{
    public const string Go2rtc = "go2rtc";
    public const string Tuya = "tuya";
    public const string R2 = "r2";

    /// <summary>Un problema de batería abierto se cierra recién con el umbral más estos puntos (evita avisos alternados).</summary>
    public const int BatteryHysteresis = 5;

    /// <summary>Con el pronóstico en estos días o menos se avisa aunque la batería siga sobre el umbral.</summary>
    public const double ForecastWarningDays = 3;

    public static string BatteryKey(int cameraId) => $"battery:{cameraId}";
    public static string SilentKey(int cameraId) => $"silent:{cameraId}";
    public static string ServiceKey(string name) => $"service:{name}";
    public const string QueueKey = "queue";
    public const string DiskKey = "disk";

    public static IReadOnlyList<HealthIssue> Evaluate(HealthFacts facts, HealthThresholds t, IReadOnlySet<string> openKeys, DateTimeOffset now)
    {
        var issues = new List<HealthIssue>();
        foreach (var camera in facts.Cameras.Where(c => c.Enabled))
        {
            if (Battery(camera, t, openKeys, now) is { } battery) issues.Add(battery);
            if (Silent(camera, t, facts.MonitorStartedAt, now) is { } silent) issues.Add(silent);
        }

        var grace = TimeSpan.FromMinutes(t.ServiceGraceMinutes);
        foreach (var s in facts.Services)
        {
            if (s.Ok || s.Since is not { } since || now - since < grace) continue;
            issues.Add(new HealthIssue(ServiceKey(s.Name), HealthSeverity.Critical, ServiceTitle(s.Name), s.Detail, null, since));
        }

        if (facts.Uploads.OldestPendingAt is { } oldest && now - oldest >= TimeSpan.FromMinutes(t.UploadStuckMinutes))
        {
            var clips = facts.Uploads.Pending == 1 ? "1 clip espera" : $"{facts.Uploads.Pending} clips esperan";
            issues.Add(new HealthIssue(QueueKey, HealthSeverity.Warning, "Hay clips sin subir",
                $"{clips} subir a R2 desde hace {Duration(now - oldest)}.", null, oldest));
        }

        if (facts.Disk is { } disk && disk.FreeBytes < t.MinFreeDiskMb * 1024L * 1024)
        {
            issues.Add(new HealthIssue(DiskKey, HealthSeverity.Critical, "Poco espacio en disco",
                $"Quedan {disk.FreeBytes / (1024 * 1024)} MB libres para clips y la cola de subida.", null, now));
        }

        return issues;
    }

    private static HealthIssue? Battery(CameraHealth c, HealthThresholds t, IReadOnlySet<string> openKeys, DateTimeOffset now)
    {
        if (c.BatteryPercent is not { } percent) return null;

        var key = BatteryKey(c.Id);
        var open = openKeys.Contains(key);
        var low = percent < t.LowBatteryPercent + (open ? BatteryHysteresis : 0);
        var soon = c.BatteryDaysLeft is { } days && days <= ForecastWarningDays + (open ? 1 : 0);
        if (!low && !soon) return null;

        var detail = $"Queda {percent} %" + (c.BatteryDaysLeft is { } left ? $", alcanza para {Days(left)}." : ".");
        return new HealthIssue(key, HealthSeverity.Warning, $"Batería baja en {c.Name}", detail, c.Id, c.DeviceInfoAt ?? now);
    }

    private static HealthIssue? Silent(CameraHealth c, HealthThresholds t, DateTimeOffset startedAt, DateTimeOffset now)
    {
        var last = c.LastAwakeAt ?? startedAt;
        if (now - last < TimeSpan.FromHours(t.SilentCameraHours)) return null;

        var detail = c.LastAwakeAt is null
            ? $"No ha despertado desde que arrancó CamGuard, hace {Duration(now - last)}."
            : $"Despertó por última vez hace {Duration(now - last)}. Revisa la batería y el Wi-Fi.";
        return new HealthIssue(SilentKey(c.Id), HealthSeverity.Warning, $"{c.Name} no despierta", detail, c.Id, last);
    }

    private static string ServiceTitle(string name) => name switch
    {
        Go2rtc => "go2rtc no responde",
        Tuya => "Sin conexión con Tuya",
        R2 => "Las subidas a R2 están fallando",
        _ => $"{name} con problemas"
    };

    /// <summary>R2 cobra por GB-mes por encima de la capa gratuita.</summary>
    public static double EstimatedMonthlyUsd(long bytes, double freeGb, double pricePerGbMonth) =>
        Math.Round(Math.Max(0, bytes / 1e9 - freeGb) * pricePerGbMonth, 2);

    public static string Duration(TimeSpan span) => span switch
    {
        { TotalMinutes: < 60 } => $"{Math.Max(1, (int)span.TotalMinutes)} min",
        { TotalHours: < 48 } => $"{(int)span.TotalHours} h",
        _ => $"{(int)span.TotalDays} días"
    };

    private static string Days(double days) => days switch
    {
        < 1 => "menos de un día",
        < 1.5 => "~1 día",
        _ => string.Create(CultureInfo.InvariantCulture, $"~{Math.Round(days):0} días")
    };
}
