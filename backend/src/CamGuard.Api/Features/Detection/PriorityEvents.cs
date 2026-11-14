using CamGuard.Api.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace CamGuard.Api.Features.Detection;

/// <summary>
/// Timbre, alarma o evento de cerradura Tuya de la cámara durante la grabación (o justo antes: el evento la
/// despertó, ver <see cref="DetectionPolicy.PriorityEventLookback"/>). Esos clips nunca se descartan y son
/// los últimos que se borran por falta de espacio (Features/Upload).
/// </summary>
public static class PriorityEvents
{
    private static IQueryable<CameraEvent> Priority(AppDbContext db) =>
        db.CameraEvents.Where(e => e.Type == CameraEventType.Doorbell || e.Type == CameraEventType.Alarm || e.Type == CameraEventType.LockEvent);

    public static Task<bool> HasPriorityEventAsync(AppDbContext db, Recording recording, CancellationToken ct)
    {
        var (from, to) = Window(recording.StartedAt, recording.EndedAt);
        return Priority(db).AnyAsync(e => e.CameraId == recording.CameraId && e.OccurredAt >= from && e.OccurredAt <= to, ct);
    }

    /// <summary>Ids de las grabaciones con evento prioritario, con una sola consulta.</summary>
    public static async Task<IReadOnlySet<Guid>> WithPriorityEventAsync(AppDbContext db, IReadOnlyCollection<Recording> recordings, CancellationToken ct)
    {
        if (recordings.Count == 0) return new HashSet<Guid>();

        var cameraIds = recordings.Select(r => r.CameraId).Distinct().ToList();
        var from = recordings.Min(r => Window(r.StartedAt, r.EndedAt).From);
        var to = recordings.Max(r => Window(r.StartedAt, r.EndedAt).To);
        var events = await Priority(db).AsNoTracking()
            .Where(e => cameraIds.Contains(e.CameraId) && e.OccurredAt >= from && e.OccurredAt <= to)
            .Select(e => new { e.CameraId, e.OccurredAt })
            .ToListAsync(ct);

        return recordings
            .Where(r =>
            {
                var (f, t) = Window(r.StartedAt, r.EndedAt);
                return events.Any(e => e.CameraId == r.CameraId && e.OccurredAt >= f && e.OccurredAt <= t);
            })
            .Select(r => r.Id)
            .ToHashSet();
    }

    private static (DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset startedAt, DateTimeOffset? endedAt) =>
        (startedAt - DetectionPolicy.PriorityEventLookback, endedAt ?? startedAt);
}
