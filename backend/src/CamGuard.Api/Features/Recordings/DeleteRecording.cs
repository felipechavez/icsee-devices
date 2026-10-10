using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Common.Storage;

namespace CamGuard.Api.Features.Recordings;

public static class DeleteRecording
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("/recordings/{id:guid}", async (Guid id, AppDbContext db, IVideoStorage storage, CancellationToken ct) =>
                {
                    var recording = await db.Recordings.FindAsync([id], ct);
                    if (recording is null)
                        return Results.NotFound();
                    if (recording.Status is RecordingStatus.Recording or RecordingStatus.Uploading)
                        return Results.Conflict(new { message = "El clip se está grabando o subiendo; inténtalo en un momento." });

                    if (recording.StorageKey is not null) await storage.DeleteAsync(recording.StorageKey, ct);
                    if (recording.ThumbnailKey is not null) await storage.DeleteAsync(recording.ThumbnailKey, ct);

                    db.Recordings.Remove(recording);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                })
                .WithTags("Recordings");
        }
    }
}
