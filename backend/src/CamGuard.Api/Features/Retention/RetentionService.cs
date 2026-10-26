using System.Linq.Expressions;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Storage;
using Microsoft.EntityFrameworkCore;

namespace CamGuard.Api.Features.Retention;

public static class RetentionPolicy
{
    public static DateTimeOffset Cutoff(DateTimeOffset now, int retentionDays) =>
        now.AddDays(-Math.Max(1, retentionDays));

    /// <summary>
    /// Solo se borran clips terminados (los descartados por la IA no tienen archivo remoto); los que están
    /// grabando, analizándose o subiendo se respetan.
    /// </summary>
    public static Expression<Func<Recording, bool>> ExpiredBefore(DateTimeOffset cutoff) =>
        r => r.StartedAt < cutoff
            && (r.Status == RecordingStatus.Uploaded || r.Status == RecordingStatus.Failed || r.Status == RecordingStatus.Discarded);
}

/// <summary>Una vez al día borra de R2 y de la base los clips más antiguos que la retención.</summary>
public class RetentionService(IServiceScopeFactory scopeFactory, IVideoStorage storage, TimeProvider clock, ILogger<RetentionService> logger)
    : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, clock, stoppingToken);
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error al aplicar la retención");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.GetSettingsAsync(ct);
        var cutoff = RetentionPolicy.Cutoff(clock.GetUtcNow(), settings.RetentionDays);

        var deleted = 0;
        while (true)
        {
            var batch = await db.Recordings
                .Where(RetentionPolicy.ExpiredBefore(cutoff))
                .OrderBy(r => r.StartedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var r in batch.Where(r => r.Status == RecordingStatus.Uploaded))
            {
                if (r.StorageKey is not null) await storage.DeleteAsync(r.StorageKey, ct);
                if (r.ThumbnailKey is not null) await storage.DeleteAsync(r.ThumbnailKey, ct);
            }

            db.Recordings.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            deleted += batch.Count;
        }

        var events = await db.CameraEvents.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        logger.LogInformation("Retención ({Days} días): {Clips} clips y {Events} eventos borrados", settings.RetentionDays, deleted, events);
    }
}
