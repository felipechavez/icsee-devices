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
/// y por ntfy. Si algo falla, reprograma el trabajo con backoff.
/// </summary>
public class UploadService(
    UploadQueue queue,
    IServiceScopeFactory scopeFactory,
    IVideoStorage storage,
    INotifier notifier,
    IHubContext<EventsHub, IEventsClient> hub,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<UploadService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var job in await queue.GetDueAsync(clock.GetUtcNow(), stoppingToken))
                {
                    await ProcessAsync(job, stoppingToken);
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

    private async Task ProcessAsync(UploadJob job, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recording = await db.Recordings.Include(r => r.Camera).FirstOrDefaultAsync(r => r.Id == job.RecordingId, ct);

        if (recording is null)
        {
            logger.LogWarning("La grabación {Id} ya no existe; se descarta el trabajo", job.RecordingId);
            Cleanup(job);
            return;
        }

        try
        {
            await storage.UploadAsync(recording.StorageKey!, job.ClipPath, "video/mp4", ct);
            if (job.ThumbnailPath is not null && recording.ThumbnailKey is not null)
                await storage.UploadAsync(recording.ThumbnailKey, job.ThumbnailPath, "image/jpeg", ct);

            recording.Status = RecordingStatus.Uploaded;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var attempts = job.Attempts + 1;
            var delay = RetryPolicy.Delay(attempts);
            logger.LogWarning(ex, "Falló la subida de {Id} (intento {Attempt}); se reintenta en {Delay}", job.RecordingId, attempts, delay);
            await queue.SaveAsync(job with { Attempts = attempts, NextAttemptAt = clock.GetUtcNow() + delay }, ct);
            return;
        }

        Cleanup(job);
        logger.LogInformation("Clip {Id} subido", recording.Id);

        await hub.Clients.All.RecordingCompleted(new RecordingCompletedMessage(recording.Id, recording.CameraId, recording.StartedAt));
        await NotifyAsync(recording, ct);
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
    public static IServiceCollection AddUploadFeature(this IServiceCollection services)
    {
        services.AddSingleton<UploadQueue>();
        services.AddHostedService<UploadService>();
        return services;
    }
}
