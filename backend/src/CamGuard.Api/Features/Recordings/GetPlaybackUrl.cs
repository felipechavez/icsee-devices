using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Common.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Recordings;

public static class GetPlaybackUrl
{
    public record Response(string Url, DateTimeOffset ExpiresAt);

    public static string DownloadFileName(Recording r) =>
        $"camguard-{r.CameraId}-{r.StartedAt.UtcDateTime:yyyyMMdd-HHmmss}.mp4";

    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/recordings/{id:guid}/play", async (
                    Guid id,
                    bool? download,
                    AppDbContext db,
                    IVideoStorage storage,
                    IOptions<StorageOptions> storageOptions,
                    TimeProvider clock,
                    CancellationToken ct) =>
                {
                    var recording = await db.Recordings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
                    if (recording is null)
                        return Results.NotFound();
                    if (recording is not { Status: RecordingStatus.Uploaded, StorageKey: not null })
                        return Results.Conflict(new { message = "El clip todavía no está disponible." });

                    var ttl = TimeSpan.FromMinutes(storageOptions.Value.PresignedUrlMinutes);
                    var url = storage.GetPresignedUrl(recording.StorageKey, ttl,
                        download == true ? DownloadFileName(recording) : null);
                    return Results.Ok(new Response(url, clock.GetUtcNow().Add(ttl)));
                })
                .WithTags("Recordings");
        }
    }
}
