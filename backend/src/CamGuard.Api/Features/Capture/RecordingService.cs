using System.Collections.Concurrent;
using CamGuard.Api.Common;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Events;
using CamGuard.Api.Common.Realtime;
using CamGuard.Api.Common.Storage;
using CamGuard.Api.Features.Upload;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Capture;

/// <summary>
/// Al despertar la cámara graba el stream RTSP de go2rtc con un tope de tiempo;
/// al dormirse corta la grabación. El clip terminado pasa a la cola de subida.
/// </summary>
public class RecordingService(
    CameraEventChannel events,
    ActivationGate gate,
    IRecorder recorder,
    UploadQueue uploads,
    IServiceScopeFactory scopeFactory,
    IOptions<Go2rtcOptions> go2rtc,
    IOptions<AppOptions> appOptions,
    IOptions<CaptureOptions> captureOptions,
    IHubContext<EventsHub, IEventsClient> hub,
    TimeProvider clock,
    ILogger<RecordingService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _stopSignals = new();
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedAsync(stoppingToken);

        try
        {
            await foreach (var change in events.Reader.ReadAllAsync(stoppingToken))
            {
                switch (change.Type)
                {
                    case CameraEventType.Awake:
                        await TryStartAsync(change, stoppingToken);
                        break;
                    case CameraEventType.Asleep when _stopSignals.TryGetValue(change.CameraId, out var stop):
                        logger.LogInformation("Cámara {Id} dormida: se corta la grabación", change.CameraId);
                        await stop.CancelAsync();
                        break;
                }
            }
        }
        finally
        {
            foreach (var stop in _stopSignals.Values) await stop.CancelAsync();
            await Task.WhenAll(_running.Values);
        }
    }

    private async Task TryStartAsync(CameraStateChanged change, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.GetSettingsAsync(ct);

        if (!gate.TryStart(change.CameraId, clock.GetUtcNow(), TimeSpan.FromSeconds(settings.CooldownSeconds)))
        {
            logger.LogInformation("Activación de la cámara {Id} ignorada (grabando o en enfriamiento)", change.CameraId);
            return;
        }

        var camera = await db.Cameras.FindAsync([change.CameraId], ct);
        if (camera is null)
        {
            gate.Finish(change.CameraId, clock.GetUtcNow());
            return;
        }

        var recording = new Recording
        {
            Id = Guid.CreateVersion7(),
            CameraId = camera.Id,
            StartedAt = clock.GetUtcNow(),
            Status = RecordingStatus.Recording
        };
        db.Recordings.Add(recording);
        await db.SaveChangesAsync(ct);
        await hub.Clients.All.RecordingStarted(new RecordingStartedMessage(recording.Id, camera.Id, recording.StartedAt));

        var stop = new CancellationTokenSource();
        _stopSignals[camera.Id] = stop;
        var maxDuration = TimeSpan.FromSeconds(settings.MaxClipSeconds);
        var rtspUrl = go2rtc.Value.RtspUrlFor(camera.Go2rtcStream);

        _running[recording.Id] = Task.Run(async () =>
        {
            try
            {
                // Sin el token del host: al apagar se cancela "stop" (ver ExecuteAsync) y FFmpeg
                // cierra el MP4 limpiamente, así el clip no se pierde y queda en la cola de subida.
                await RecordAsync(recording.Id, camera.Id, rtspUrl, maxDuration, stop.Token);
            }
            finally
            {
                // Sin Dispose: el apagado podría cancelarlo al mismo tiempo y no tiene timers que liberar.
                _stopSignals.TryRemove(new KeyValuePair<int, CancellationTokenSource>(camera.Id, stop));
                gate.Finish(camera.Id, clock.GetUtcNow());
                _running.TryRemove(recording.Id, out _);
            }
        }, CancellationToken.None);
    }

    private (string Clip, string Thumbnail) LocalPaths(Guid recordingId)
    {
        var clipsDir = Path.Combine(appOptions.Value.DataDirectory, "clips");
        Directory.CreateDirectory(clipsDir);
        return (Path.Combine(clipsDir, $"{recordingId:N}.mp4"), Path.Combine(clipsDir, $"{recordingId:N}.jpg"));
    }

    private async Task RecordAsync(Guid recordingId, int cameraId, string rtspUrl, TimeSpan maxDuration, CancellationToken stop)
    {
        var (clipPath, thumbPath) = LocalPaths(recordingId);

        logger.LogInformation("Grabando {Id} (máx. {Max} s)", recordingId, maxDuration.TotalSeconds);

        RecordResult result;
        try
        {
            result = await recorder.RecordAsync(rtspUrl, clipPath, maxDuration, stop, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FFmpeg falló al grabar {Id}", recordingId);
            await MarkFailedAsync(recordingId, clipPath, CancellationToken.None);
            return;
        }

        var file = new FileInfo(clipPath);
        if (!file.Exists || file.Length < captureOptions.Value.MinClipBytes)
        {
            logger.LogWarning("El clip {Id} quedó vacío (código {Code})", recordingId, result.ExitCode);
            await MarkFailedAsync(recordingId, clipPath, CancellationToken.None);
            return;
        }

        var hasThumb = await recorder.CreateThumbnailAsync(clipPath, thumbPath, CancellationToken.None);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recording = await db.Recordings.FindAsync([recordingId], CancellationToken.None);
        if (recording is null) return;

        recording.EndedAt = recording.StartedAt + result.Elapsed;
        recording.DurationSec = Math.Round(result.Elapsed.TotalSeconds, 1);
        recording.SizeBytes = file.Length;
        recording.StorageKey = StorageKeys.Clip(cameraId, recordingId, recording.StartedAt);
        recording.ThumbnailKey = hasThumb ? StorageKeys.Thumbnail(cameraId, recordingId, recording.StartedAt) : null;
        recording.Status = RecordingStatus.Uploading;
        await db.SaveChangesAsync(CancellationToken.None);

        await uploads.EnqueueAsync(new UploadJob(recordingId, clipPath, hasThumb ? thumbPath : null), CancellationToken.None);
    }

    private async Task MarkFailedAsync(Guid recordingId, string clipPath, CancellationToken ct)
    {
        try
        {
            File.Delete(clipPath);
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recording = await db.Recordings.FindAsync([recordingId], ct);
            if (recording is null) return;
            recording.Status = RecordingStatus.Failed;
            recording.EndedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo marcar {Id} como fallida", recordingId);
        }
    }

    /// <summary>
    /// Grabaciones que quedaron en "Recording" por un corte (luz, caída del proceso) pasan
    /// a Failed y se borran sus archivos locales: un MP4 sin cerrar no se puede reproducir.
    /// </summary>
    private async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stale = await db.Recordings.Where(r => r.Status == RecordingStatus.Recording).ToListAsync(ct);
        foreach (var r in stale)
        {
            r.Status = RecordingStatus.Failed;
            r.EndedAt ??= clock.GetUtcNow();
            var (clip, thumb) = LocalPaths(r.Id);
            File.Delete(clip);
            File.Delete(thumb);
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning("{Count} grabaciones interrumpidas marcadas como fallidas", stale.Count);
        }
    }
}

public static class CaptureFeature
{
    public static IServiceCollection AddCaptureFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CaptureOptions>(configuration.GetSection(CaptureOptions.Section));
        services.AddSingleton<ActivationGate>();
        services.AddSingleton<IRecorder, FfmpegRecorder>();
        services.AddHostedService<RecordingService>();
        return services;
    }
}
