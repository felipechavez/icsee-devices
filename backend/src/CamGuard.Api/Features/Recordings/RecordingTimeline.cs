using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace CamGuard.Api.Features.Recordings;

/// <summary>
/// Marcas livianas de un rango (sin paginar ni URLs) para la línea de tiempo de 24 h
/// y el histograma "Actividad hoy".
/// </summary>
public static class RecordingTimeline
{
    public const int MaxItems = 2000;
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(2);

    public record Item(Guid Id, int CameraId, DateTimeOffset StartedAt, RecordingStatus Status);

    public static Dictionary<string, string[]> Validate(DateTimeOffset from, DateTimeOffset to)
    {
        var errors = new Dictionary<string, string[]>();
        if (to <= from || to - from > MaxRange)
            errors["to"] = ["El rango debe ser positivo y de máx. 2 días."];
        return errors;
    }

    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/recordings/timeline", async (
                    DateTimeOffset from,
                    DateTimeOffset to,
                    int? cameraId,
                    AppDbContext db,
                    CancellationToken ct) =>
                {
                    var errors = Validate(from, to);
                    if (errors.Count > 0)
                        return Results.ValidationProblem(errors);

                    var query = db.Recordings.AsNoTracking().Where(r => r.StartedAt >= from && r.StartedAt < to);
                    if (cameraId is not null) query = query.Where(r => r.CameraId == cameraId);

                    var items = await query
                        .OrderByDescending(r => r.StartedAt)
                        .Take(MaxItems)
                        .Select(r => new Item(r.Id, r.CameraId, r.StartedAt, r.Status))
                        .ToListAsync(ct);
                    return Results.Ok(items);
                })
                .WithTags("Recordings");
        }
    }
}

/// <summary>Total de clips guardados, para el encabezado de Grabaciones.</summary>
public static class RecordingSummary
{
    public record Response(int TotalCount);

    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/recordings/summary", async (AppDbContext db, CancellationToken ct) =>
                    new Response(await db.Recordings.CountAsync(r => r.Status == RecordingStatus.Uploaded, ct)))
                .WithTags("Recordings");
        }
    }
}
