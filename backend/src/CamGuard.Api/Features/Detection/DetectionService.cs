using System.Diagnostics;
using CamGuard.Api.Common;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Realtime;
using CamGuard.Api.Common.Storage;
using CamGuard.Api.Features.Upload;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Detection;

/// <summary>
/// Analiza cada clip grabado, uno a la vez, después de grabar (en la Pi la IA nunca compite con FFmpeg
/// grabando). Según el modo de la cámara lo etiqueta y lo pasa a la cola de subida, o lo descarta si
/// no tiene personas ni animales. Ante cualquier falla el clip se sube igual (fail-open).
/// </summary>
public class DetectionService(
    DetectionQueue queue,
    FrameSampler sampler,
    IObjectDetector detector,
    UploadQueue uploads,
    IServiceScopeFactory scopeFactory,
    IHubContext<EventsHub, IEventsClient> hub,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<DetectionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeuePendingAsync(stoppingToken);

        try
        {
            await foreach (var recordingId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(recordingId, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Queda en Analyzing y se reintenta al reiniciar.
                    logger.LogError(ex, "Error al procesar el clip {Id}", recordingId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Lo que estaba en análisis sigue en Analyzing y se retoma al arrancar.
        }
    }

    private async Task ProcessAsync(Guid recordingId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recording = await db.Recordings.Include(r => r.Camera).FirstOrDefaultAsync(r => r.Id == recordingId, ct);
        if (recording is not { Status: RecordingStatus.Analyzing, Camera: { } camera }) return;

        var (clipPath, thumbPath) = ClipPaths.For(appOptions.Value.DataDirectory, recordingId);
        if (!File.Exists(clipPath))
        {
            logger.LogWarning("El clip {Id} ya no está en disco: se marca como fallido", recordingId);
            recording.Status = RecordingStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var settings = await db.GetSettingsAsync(ct);
        var verdict = camera.DetectionMode == DetectionMode.Off
            ? null
            : await AnalyzeAsync(clipPath, camera, settings.DetectionMinConfidence, ct);
        var hasPriorityEvent = camera.DetectionMode == DetectionMode.Filter && verdict is { Found: false }
            && await HasPriorityEventAsync(db, recording, ct);
        var decision = DetectionPolicy.Decide(camera.DetectionMode, verdict, hasPriorityEvent);

        recording.Detection = verdict?.Label;
        recording.DetectionScore = verdict is null ? null : Math.Round(verdict.Score, 3);

        if (decision == DetectionDecision.Discard)
        {
            // Sin archivo remoto: la retención solo borra la fila.
            recording.Status = RecordingStatus.Discarded;
            recording.StorageKey = null;
            recording.ThumbnailKey = null;
            await db.SaveChangesAsync(ct);
            TryDelete(clipPath);
            TryDelete(thumbPath);
            logger.LogInformation("Clip {Id} de {Camera}: {Label} ({Score:0.00}) → descartado ({Ms} ms)",
                recordingId, camera.Name, verdict!.Label, verdict.Score, stopwatch.ElapsedMilliseconds);
            await hub.Clients.All.RecordingCompleted(new RecordingCompletedMessage(recording.Id, recording.CameraId, recording.StartedAt));
            return;
        }

        recording.Status = RecordingStatus.Uploading;
        await db.SaveChangesAsync(ct);
        await uploads.EnqueueAsync(new UploadJob(recordingId, clipPath, recording.ThumbnailKey is not null ? thumbPath : null), CancellationToken.None);

        if (verdict is null)
            logger.LogInformation("Clip {Id} de {Camera}: sin analizar → se sube", recordingId, camera.Name);
        else
            logger.LogInformation("Clip {Id} de {Camera}: {Label} ({Score:0.00}) → se sube{Priority} ({Ms} ms)",
                recordingId, camera.Name, verdict.Label, verdict.Score, hasPriorityEvent ? " (timbre o alarma)" : "",
                stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Analiza cuadro por cuadro y corta en el primer positivo. Null si no se pudo analizar: modelo
    /// ausente, FFmpeg con error o excepción de ONNX (el clip se sube igual).
    /// </summary>
    private async Task<DetectionVerdict?> AnalyzeAsync(string clipPath, Camera camera, double minConfidence, CancellationToken ct)
    {
        if (!detector.IsAvailable) return null;

        try
        {
            var seen = new List<ObjectDetection>();
            var frames = 0;
            await foreach (var frame in sampler.SampleAsync(clipPath, ct))
            {
                frames++;
                var detections = detector.Detect(frame);
                seen.AddRange(detections);
                if (DetectionPolicy.Evaluate(detections, camera.DetectPeople, camera.DetectAnimals, minConfidence).Found) break;
            }

            if (frames == 0)
            {
                logger.LogWarning("El clip de {Camera} no entregó cuadros para analizar: se sube sin analizar", camera.Name);
                return null;
            }

            return DetectionPolicy.Evaluate(seen, camera.DetectPeople, camera.DetectAnimals, minConfidence);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falló el análisis del clip de {Camera}: se sube sin analizar", camera.Name);
            return null;
        }
    }

    /// <summary>Timbre o alarma Tuya de la cámara durante la grabación (o justo antes: el evento la despertó).</summary>
    private static Task<bool> HasPriorityEventAsync(AppDbContext db, Recording recording, CancellationToken ct)
    {
        var from = recording.StartedAt - DetectionPolicy.PriorityEventLookback;
        var to = recording.EndedAt ?? recording.StartedAt;
        return db.CameraEvents.AnyAsync(e => e.CameraId == recording.CameraId
            && (e.Type == CameraEventType.Doorbell || e.Type == CameraEventType.Alarm || e.Type == CameraEventType.LockEvent)
            && e.OccurredAt >= from && e.OccurredAt <= to, ct);
    }

    /// <summary>Grabaciones que quedaron en Analyzing (reinicio, corte de luz) se vuelven a analizar.</summary>
    private async Task RequeuePendingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.Recordings.Where(r => r.Status == RecordingStatus.Analyzing).OrderBy(r => r.StartedAt).ToListAsync(ct);
        var lost = 0;
        foreach (var r in pending)
        {
            if (File.Exists(ClipPaths.For(appOptions.Value.DataDirectory, r.Id).Clip))
            {
                queue.Enqueue(r.Id);
                continue;
            }

            r.Status = RecordingStatus.Failed;
            r.EndedAt ??= clock.GetUtcNow();
            lost++;
        }

        if (lost > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning("{Count} clips por analizar ya no estaban en disco: marcados como fallidos", lost);
        }
        if (pending.Count > lost)
            logger.LogInformation("{Count} clips pendientes de análisis reencolados", pending.Count - lost);
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

public static class DetectionFeature
{
    public static IServiceCollection AddDetectionFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DetectionOptions>(configuration.GetSection(DetectionOptions.Section));
        services.AddSingleton<DetectionQueue>();
        services.AddSingleton<FrameSampler>();
        services.AddSingleton<IObjectDetector, OnnxObjectDetector>();
        services.AddHostedService<DetectionService>();
        return services;
    }
}
