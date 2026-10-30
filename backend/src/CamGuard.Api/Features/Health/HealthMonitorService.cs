using CamGuard.Api.Common;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Notifications;
using CamGuard.Api.Common.Realtime;
using CamGuard.Api.Features.Tuya;
using CamGuard.Api.Features.Upload;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Health;

/// <summary>Último estado del sistema calculado por <see cref="HealthMonitorService"/>; null hasta la primera revisión.</summary>
public class HealthSnapshotStore
{
    private SystemStatus? _current;

    public SystemStatus? Current => Volatile.Read(ref _current);

    public void Set(SystemStatus status) => Volatile.Write(ref _current, status);
}

/// <summary>
/// Cada <see cref="HealthOptions.CheckMinutes"/> reúne batería, actividad de las cámaras, servicios, cola y disco;
/// guarda el historial de batería, evalúa los problemas y los avisa por ntfy. Solo lee lo que ya se sabe:
/// nunca despierta una cámara.
/// </summary>
public class HealthMonitorService(
    IServiceScopeFactory scopeFactory,
    HealthSnapshotStore store,
    UploadHealth uploads,
    TuyaConnectionState tuya,
    IOptions<TuyaOptions> tuyaOptions,
    IHttpClientFactory httpFactory,
    INotifier notifier,
    IHubContext<EventsHub, IEventsClient> hub,
    IOptions<HealthOptions> options,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<HealthMonitorService> logger) : BackgroundService
{
    private const int ForecastDays = 30;
    private const int ChartDays = 14;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly DateTimeOffset _startedAt = clock.GetUtcNow();
    private (bool Ok, DateTimeOffset Since)? _go2rtc;
    private DateTimeOffset _lastPruneAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, clock, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.CheckMinutes)), clock);
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Error al revisar el estado del sistema");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.GetSettingsAsync(ct);

        var cameras = await GatherCamerasAsync(db, now, ct);
        var services = await GatherServicesAsync(now, ct);
        var uploadsHealth = await GatherUploadsAsync(db, ct);
        var storage = await GatherStorageAsync(db, o, ct);
        var facts = new HealthFacts(cameras, services, uploadsHealth, ReadDisk(), storage, _startedAt);

        var alerts = await db.HealthAlerts.ToDictionaryAsync(a => a.Key, ct);
        var thresholds = new HealthThresholds(settings.LowBatteryPercent, settings.SilentCameraHours,
            o.UploadStuckMinutes, o.MinFreeDiskMb, o.ServiceGraceMinutes);
        var issues = HealthEvaluator.Evaluate(facts, thresholds, alerts.Keys.ToHashSet(StringComparer.Ordinal), now);
        var decision = HealthAlertPolicy.Decide(issues, alerts, now, TimeSpan.FromHours(o.RepeatHours));

        foreach (var issue in decision.ToOpen)
        {
            logger.LogWarning("Problema del sistema: {Title}. {Detail}", issue.Title, issue.Detail);
            var alert = new HealthAlert { Key = issue.Key, Title = issue.Title, OpenedAt = now };
            db.HealthAlerts.Add(alert);
            alerts[issue.Key] = alert;
        }

        if (settings.HealthAlertsEnabled)
        {
            foreach (var issue in decision.ToNotify)
            {
                if (await TryNotifyAsync($"Problema: {issue.Title}", issue.Detail ?? "", 4, "warning", ct))
                    alerts[issue.Key].LastNotifiedAt = now;
            }
        }

        foreach (var alert in decision.ToResolve)
        {
            logger.LogInformation("Problema resuelto: {Title}", alert.Title);
            if (settings.HealthAlertsEnabled && HealthAlertPolicy.NotifyResolution(alert))
                await TryNotifyAsync($"Resuelto: {alert.Title}", "Ya no se detecta el problema.", 2, "white_check_mark", ct);
            db.HealthAlerts.Remove(alert);
            alerts.Remove(alert.Key);
        }

        await db.SaveChangesAsync(ct);

        // En el panel, "desde" es lo más antiguo entre lo que dice el problema y cuándo se abrió.
        var shown = issues
            .Select(i => alerts.TryGetValue(i.Key, out var a) && a.OpenedAt < i.Since ? i with { Since = a.OpenedAt } : i)
            .OrderByDescending(i => i.Severity).ThenBy(i => i.Since)
            .ToList();
        var previous = store.Current;
        store.Set(new SystemStatus(now, shown, cameras, services, uploadsHealth, facts.Disk, storage));
        if (previous is null || !previous.Issues.Select(i => i.Key).ToHashSet().SetEquals(shown.Select(i => i.Key)))
            await hub.Clients.All.HealthUpdated(new HealthUpdatedMessage(shown.Count));

        if (now - _lastPruneAt >= TimeSpan.FromDays(1))
        {
            _lastPruneAt = now;
            var cutoff = now.AddDays(-Math.Max(ForecastDays, o.BatteryHistoryDays));
            await db.BatteryReadings.Where(b => b.At < cutoff).ExecuteDeleteAsync(ct);
        }
    }

    /// <summary>Guarda las lecturas de batería nuevas (las escriben Devices y Tuya en la cámara) y arma el estado de cada una.</summary>
    private async Task<IReadOnlyList<CameraHealth>> GatherCamerasAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cameras = await db.Cameras.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);

        var historyFrom = now.AddDays(-ForecastDays);
        var readings = (await db.BatteryReadings.AsNoTracking()
                .Where(b => b.At >= historyFrom)
                .OrderBy(b => b.At)
                .ToListAsync(ct))
            .GroupBy(b => b.CameraId)
            .ToDictionary(g => g.Key, g => g.Select(b => new BatteryPoint(b.At, b.Percent)).ToList());

        var weekAgo = now.AddDays(-7);
        var awake = await db.CameraEvents.AsNoTracking()
            .Where(e => e.Type == CameraEventType.Awake)
            .GroupBy(e => e.CameraId)
            .Select(g => new { CameraId = g.Key, Last = g.Max(e => e.OccurredAt), Week = g.Count(e => e.OccurredAt >= weekAgo) })
            .ToDictionaryAsync(x => x.CameraId, ct);

        var chartFrom = now.AddDays(-ChartDays);
        var result = new List<CameraHealth>();
        foreach (var c in cameras)
        {
            var points = readings.GetValueOrDefault(c.Id) ?? [];
            if (c.BatteryPercent is { } percent && c.DeviceInfoAt is { } at && BatteryHistory.ShouldRecord(points.LastOrDefault(), percent, at))
            {
                db.BatteryReadings.Add(new BatteryReading { CameraId = c.Id, Percent = percent, At = at });
                points.Add(new BatteryPoint(at, percent));
            }

            var activity = awake.GetValueOrDefault(c.Id);
            result.Add(new CameraHealth(
                c.Id, c.Name, c.Kind, c.Enabled, c.BatteryPercent, c.DeviceInfoAt,
                points.Count > 0 ? BatteryForecast.DaysLeft(points, now) : null,
                points.Where(p => p.At >= chartFrom).ToList(),
                activity?.Last,
                Math.Round((activity?.Week ?? 0) / 7.0, 1)));
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<IReadOnlyList<ServiceHealth>> GatherServicesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var services = new List<ServiceHealth> { await CheckGo2rtcAsync(now, ct) };

        if (tuyaOptions.Value.IsConfigured)
        {
            var c = tuya.Current;
            services.Add(new ServiceHealth(HealthEvaluator.Tuya, c.Connected, c.Since, c.LastError));
        }

        var u = uploads.Current;
        services.Add(new ServiceHealth(HealthEvaluator.R2, u.FailingSince is null, u.FailingSince ?? u.LastSuccessAt, u.LastError));
        return services;
    }

    private async Task<ServiceHealth> CheckGo2rtcAsync(DateTimeOffset now, CancellationToken ct)
    {
        string? error = null;
        try
        {
            using var response = await httpFactory.CreateClient(HealthFeature.Go2rtcClientName).GetAsync("api", ct);
            if (!response.IsSuccessStatusCode) error = $"HTTP {(int)response.StatusCode}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            error = ex.Message;
        }

        var ok = error is null;
        if (_go2rtc is not { } previous || previous.Ok != ok) _go2rtc = (ok, now);
        return new ServiceHealth(HealthEvaluator.Go2rtc, ok, _go2rtc.Value.Since, error);
    }

    private static async Task<UploadsHealth> GatherUploadsAsync(AppDbContext db, CancellationToken ct)
    {
        var uploading = db.Recordings.AsNoTracking().Where(r => r.Status == RecordingStatus.Uploading);
        var pending = await uploading.CountAsync(ct);
        var oldest = pending > 0 ? await uploading.MinAsync(r => (DateTimeOffset?)r.StartedAt, ct) : null;
        var analyzing = await db.Recordings.CountAsync(r => r.Status == RecordingStatus.Analyzing, ct);
        return new UploadsHealth(pending, analyzing, oldest);
    }

    private static async Task<StorageUsage> GatherStorageAsync(AppDbContext db, HealthOptions o, CancellationToken ct)
    {
        var uploaded = db.Recordings.AsNoTracking().Where(r => r.Status == RecordingStatus.Uploaded);
        var bytes = await uploaded.SumAsync(r => r.SizeBytes ?? 0, ct);
        var clips = await uploaded.CountAsync(ct);
        return new StorageUsage(bytes, clips, HealthEvaluator.EstimatedMonthlyUsd(bytes, o.R2FreeGb, o.R2PricePerGbMonth));
    }

    private DiskHealth? ReadDisk()
    {
        try
        {
            var drive = new DriveInfo(Path.GetFullPath(appOptions.Value.DataDirectory));
            return new DiskHealth(drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logger.LogDebug("No se pudo leer el espacio en disco: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>Si ntfy falla, el problema queda sin <c>LastNotifiedAt</c> y se reintenta en la próxima revisión.</summary>
    private async Task<bool> TryNotifyAsync(string title, string message, int priority, string tag, CancellationToken ct)
    {
        try
        {
            await notifier.NotifyAsync(new Notification(
                Title: title,
                Message: message,
                ClickUrl: $"{appOptions.Value.PublicBaseUrl.TrimEnd('/')}/status",
                Tags: [tag],
                Priority: priority), ct);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("No se pudo enviar el aviso \"{Title}\": {Message}", title, ex.Message);
            return false;
        }
    }
}

public static class HealthFeature
{
    public const string Go2rtcClientName = "health-go2rtc";

    public static IServiceCollection AddHealthFeature(this IServiceCollection services, IConfiguration configuration)
    {
        var go2rtc = configuration.GetSection(Go2rtcOptions.Section).Get<Go2rtcOptions>() ?? new Go2rtcOptions();
        services.Configure<HealthOptions>(configuration.GetSection(HealthOptions.Section));
        services.AddHttpClient(Go2rtcClientName, c =>
        {
            go2rtc.ConfigureHttpClient(c);
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<HealthSnapshotStore>();
        services.AddHostedService<HealthMonitorService>();
        return services;
    }
}
