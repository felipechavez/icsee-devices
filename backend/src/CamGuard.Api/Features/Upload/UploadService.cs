using CamGuard.Api.Common;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Notifications;
using CamGuard.Api.Common.Realtime;
using CamGuard.Api.Common.Storage;
using CamGuard.Api.Features.Detection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Upload;

/// <summary>
/// Sube clip y miniatura, marca la grabación como Uploaded, avisa por SignalR
/// y por ntfy. Si algo falla, reprograma el trabajo con backoff y pausa toda la cola
/// (con R2 caído, N clips no deben ser N timeouts por ciclo). Sin internet, además, mantiene
/// los clips en espera bajo <see cref="AppSettings.LocalMaxGb"/> (ver <see cref="LocalEvictionPolicy"/>).
/// </summary>
public class UploadService(
    UploadQueue queue,
    UploadHealth health,
    IServiceScopeFactory scopeFactory,
    IVideoStorage storage,
    INotifier notifier,
    IHubContext<EventsHub, IEventsClient> hub,
    IOptions<AppOptions> appOptions,
    IOptions<UploadOptions> uploadOptions,
    TimeProvider clock,
    ILogger<UploadService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EvictionInterval = TimeSpan.FromMinutes(1);

    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastEvictionAt = DateTimeOffset.MinValue;
    private int _consecutiveFailures;

    /// <summary>¿Se procesa el lote? No mientras dure la pausa que dejó el último fallo.</summary>
    public static bool ShouldProcess(DateTimeOffset now, DateTimeOffset pausedUntil) => now >= pausedUntil;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = clock.GetUtcNow();
                if (now - _lastEvictionAt >= EvictionInterval)
                {
                    // Es el único hilo que toca la cola: no compite con una subida en curso.
                    _lastEvictionAt = now;
                    await EvictIfNeededAsync(stoppingToken);
                }

                if (ShouldProcess(now, _pausedUntil))
                {
                    foreach (var job in await queue.GetDueAsync(now, stoppingToken))
                    {
                        if (!await ProcessAsync(job, stoppingToken)) break;
                    }
                }

                await queue.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en el ciclo de subida");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>False si la subida falló: se corta el lote y la cola queda en pausa.</summary>
    private async Task<bool> ProcessAsync(UploadJob job, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recording = await db.Recordings.Include(r => r.Camera).FirstOrDefaultAsync(r => r.Id == job.RecordingId, ct);

        if (recording is null)
        {
            logger.LogWarning("La grabación {Id} ya no existe; se descarta el trabajo", job.RecordingId);
            Cleanup(job);
            return true;
        }

        try
        {
            await storage.UploadAsync(recording.StorageKey!, job.ClipPath, "video/mp4", ct);
            if (job.ThumbnailPath is not null && recording.ThumbnailKey is not null)
                await storage.UploadAsync(recording.ThumbnailKey, job.ThumbnailPath, "image/jpeg", ct);

            health.Succeeded();
            _pausedUntil = DateTimeOffset.MinValue;
            _consecutiveFailures = 0;

            recording.Status = RecordingStatus.Uploaded;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            health.Failed(ex.Message);
            var attempts = job.Attempts + 1;
            // Tras la pausa puede fallar otro clip con menos intentos: el backoff sigue los fallos seguidos de la cola.
            _consecutiveFailures++;
            var delay = RetryPolicy.Delay(Math.Max(attempts, _consecutiveFailures));
            _pausedUntil = clock.GetUtcNow() + delay;
            logger.LogWarning(ex, "Falló la subida de {Id} (intento {Attempt}); la cola se pausa {Delay}", job.RecordingId, attempts, delay);
            await queue.SaveAsync(job with { Attempts = attempts, NextAttemptAt = _pausedUntil }, ct);
            return false;
        }

        Cleanup(job);
        logger.LogInformation("Clip {Id} subido", recording.Id);

        await hub.Clients.All.RecordingCompleted(new RecordingCompletedMessage(recording.Id, recording.CameraId, recording.StartedAt));
        await NotifyAsync(recording, ct);
        return true;
    }

    /// <summary>
    /// Si los clips en {DataDirectory}/clips pasan <see cref="AppSettings.LocalMaxGb"/> o queda menos de
    /// <see cref="UploadOptions.MinFreeDiskMb"/> libre, borra clips en espera por prioridad y los marca Evicted.
    /// Solo los Uploading: los Recording están abiertos por FFmpeg y los Analyzing pasan a Uploading en segundos.
    /// </summary>
    private async Task EvictIfNeededAsync(CancellationToken ct)
    {
        var dataDirectory = appOptions.Value.DataDirectory;
        var clipsDir = Path.GetDirectoryName(ClipPaths.For(dataDirectory, Guid.Empty).Clip)!;
        var used = DirectorySize(clipsDir);
        long free;
        try
        {
            free = new DriveInfo(Path.GetFullPath(dataDirectory)).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            free = long.MaxValue / 2; // sin dato de disco, solo se aplica el tope
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.GetSettingsAsync(ct);
        var maxBytes = settings.LocalMaxGb * (1L << 30);
        var minFree = uploadOptions.Value.MinFreeDiskMb * (1L << 20);
        if (!LocalEvictionPolicy.IsOver(used, free, maxBytes, minFree)) return;

        var waiting = await db.Recordings.Where(r => r.Status == RecordingStatus.Uploading).ToListAsync(ct);
        var priority = await PriorityEvents.WithPriorityEventAsync(db, waiting, ct);
        var candidates = waiting.Select(r =>
        {
            var (clip, thumb) = ClipPaths.For(dataDirectory, r.Id);
            return new EvictionCandidate(r.Id, r.StartedAt, FileSize(clip) + FileSize(thumb),
                LocalEvictionPolicy.Tier(r.Detection, priority.Contains(r.Id)));
        });

        var selected = LocalEvictionPolicy.Select(candidates, used, free, maxBytes, minFree);
        if (selected.Count == 0) return;

        var byId = waiting.ToDictionary(r => r.Id);
        foreach (var c in selected)
        {
            var recording = byId[c.Id];
            var (clip, thumb) = ClipPaths.For(dataDirectory, c.Id);
            queue.Remove(c.Id);
            TryDelete(clip);
            TryDelete(thumb);
            recording.Status = RecordingStatus.Evicted;
            recording.StorageKey = null;
            recording.ThumbnailKey = null;
            logger.LogWarning("Sin espacio para clips en espera: se borró sin subir el clip {Id} ({Tier}, {StartedAt:u}, {Mb:0.0} MB)",
                c.Id, c.Tier, c.StartedAt, c.Bytes / 1048576.0);
        }

        await db.SaveChangesAsync(ct);
        foreach (var c in selected)
        {
            var r = byId[c.Id];
            await hub.Clients.All.RecordingCompleted(new RecordingCompletedMessage(r.Id, r.CameraId, r.StartedAt));
        }
    }

    private static long DirectorySize(string directory)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory))
            total += FileSize(file);
        return total;
    }

    private static long FileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private async Task NotifyAsync(Recording recording, CancellationToken ct)
    {
        try
        {
            var link = $"{appOptions.Value.PublicBaseUrl.TrimEnd('/')}/recordings?clip={recording.Id}";
            var image = recording.ThumbnailKey is not null
                ? storage.GetPresignedUrl(recording.ThumbnailKey, TimeSpan.FromHours(24))
                : null;
            var duration = recording.DurationSec is { } d ? $" ({Math.Round(d)} s)" : "";

            await notifier.NotifyAsync(new Notification(
                Title: NotificationTitle(recording.Detection, recording.Camera?.Name),
                Message: $"Nuevo clip a las {recording.StartedAt.ToLocalTime():HH:mm:ss}{duration}",
                ClickUrl: link,
                ImageUrl: image,
                Tags: ["video_camera"]), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // La notificación no es crítica: el clip ya está a salvo.
            logger.LogWarning(ex, "No se pudo enviar la notificación de {Id}", recording.Id);
        }
    }

    /// <summary>"Persona: Entrada", "Animal (perro): Patio"; sin detección (o "nada"), "Movimiento: …".</summary>
    public static string NotificationTitle(string? detection, string? cameraName) => detection switch
    {
        CocoClasses.PersonLabel => $"Persona: {cameraName}",
        _ when CocoClasses.IsAnimalLabel(detection) => $"Animal ({detection}): {cameraName}",
        _ => $"Movimiento: {cameraName}"
    };

    private void Cleanup(UploadJob job)
    {
        queue.Remove(job.RecordingId);
        TryDelete(job.ClipPath);
        if (job.ThumbnailPath is not null) TryDelete(job.ThumbnailPath);
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "No se pudo borrar {Path}", path);
        }
    }
}

public static class UploadFeature
{
    public static IServiceCollection AddUploadFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UploadOptions>(configuration.GetSection(UploadOptions.Section));
        services.AddSingleton<UploadQueue>();
        services.AddSingleton<UploadHealth>();
        services.AddHostedService<UploadService>();
        return services;
    }
}
