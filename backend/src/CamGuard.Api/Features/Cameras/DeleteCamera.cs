using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Features.Monitoring;
using CamGuard.Api.Features.Streams;
using Microsoft.EntityFrameworkCore;

namespace CamGuard.Api.Features.Cameras;

public static class DeleteCamera
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("/cameras/{id:int}", async (
                    int id,
                    AppDbContext db,
                    CameraStateStore states,
                    StreamSyncTrigger sync,
                    CancellationToken ct) =>
                {
                    var camera = await db.Cameras.FindAsync([id], ct);
                    if (camera is null)
                        return Results.NotFound();

                    // El borrado en cascada eliminaría las filas pero dejaría los clips huérfanos en R2.
                    if (await db.Recordings.AnyAsync(r => r.CameraId == id, ct))
                        return Results.Problem(
                            "La cámara tiene grabaciones. Deshabilítala para conservar su historial; podrás eliminarla cuando la retención las borre.",
                            statusCode: StatusCodes.Status409Conflict);

                    db.Cameras.Remove(camera);
                    await db.SaveChangesAsync(ct);
                    states.Remove(id);
                    sync.Request();
                    return Results.NoContent();
                })
                .WithTags("Cameras");
        }
    }
}
