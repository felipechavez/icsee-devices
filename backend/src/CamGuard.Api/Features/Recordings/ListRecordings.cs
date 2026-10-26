using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Common.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Recordings;

public static class ListRecordings
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 100;

    public record RecordingResponse(
        Guid Id,
        int CameraId,
        string CameraName,
        DateTimeOffset StartedAt,
        DateTimeOffset? EndedAt,
        double? DurationSec,
        long? SizeBytes,
        RecordingStatus Status,
        string? ThumbnailUrl);

    public record Response(IReadOnlyList<RecordingResponse> Items, int Page, int PageSize, int TotalCount, bool HasMore);

    public static (int Page, int PageSize) NormalizePaging(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));

    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/recordings", async (
                    DateTimeOffset? from,
                    DateTimeOffset? to,
                    int? cameraId,
                    RecordingStatus? status,
                    int? page,
                    int? pageSize,
                    AppDbContext db,
                    IVideoStorage storage,
                    IOptions<StorageOptions> storageOptions,
                    CancellationToken ct) =>
                {
                    var (p, size) = NormalizePaging(page, pageSize);

                    // Postgres (timestamptz) solo acepta DateTimeOffset en UTC; el cliente puede mandar su zona.
                    var fromUtc = from?.ToUniversalTime();
                    var toUtc = to?.ToUniversalTime();
                    var query = db.Recordings.AsNoTracking();
                    if (fromUtc is not null) query = query.Where(r => r.StartedAt >= fromUtc);
                    if (toUtc is not null) query = query.Where(r => r.StartedAt < toUtc);
                    if (cameraId is not null) query = query.Where(r => r.CameraId == cameraId);
                    if (status is not null) query = query.Where(r => r.Status == status);

                    var total = await query.CountAsync(ct);
                    var rows = await query
                        .OrderByDescending(r => r.StartedAt)
                        .Skip((p - 1) * size)
                        .Take(size)
                        .Select(r => new { Recording = r, CameraName = r.Camera!.Name })
                        .ToListAsync(ct);

                    var ttl = TimeSpan.FromMinutes(storageOptions.Value.PresignedUrlMinutes);
                    var items = rows.Select(x =>
                    {
                        var r = x.Recording;
                        var thumb = r is { Status: RecordingStatus.Uploaded, ThumbnailKey: not null }
                            ? storage.GetPresignedUrl(r.ThumbnailKey, ttl)
                            : null;
                        return new RecordingResponse(r.Id, r.CameraId, x.CameraName, r.StartedAt, r.EndedAt,
                            r.DurationSec, r.SizeBytes, r.Status, thumb);
                    }).ToList();

                    return new Response(items, p, size, total, p * size < total);
                })
                .WithTags("Recordings");
        }
    }
}
