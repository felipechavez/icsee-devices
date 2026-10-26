using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Common.Security;
using CamGuard.Api.Features.Streams;

namespace CamGuard.Api.Features.Cameras;

public static class UpdateCamera
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPut("/cameras/{id:int}", async (
                    int id,
                    CameraInput request,
                    AppDbContext db,
                    CameraSecretProtector secrets,
                    StreamSyncTrigger sync,
                    CancellationToken ct) =>
                {
                    var errors = CameraInput.Validate(request, isNew: false);
                    if (errors.Count > 0)
                        return Results.ValidationProblem(errors);

                    var camera = await db.Cameras.FindAsync([id], ct);
                    if (camera is null)
                        return Results.NotFound();

                    errors = CameraInput.ValidateKindUnchanged(request, camera);
                    if (errors.Count > 0)
                        return Results.ValidationProblem(errors);

                    errors = await request.ValidateUniqueAsync(db, id, ct);
                    if (errors.Count > 0)
                        return Results.ValidationProblem(errors, statusCode: StatusCodes.Status409Conflict);

                    request.ApplyTo(camera, secrets);
                    await db.SaveChangesAsync(ct);
                    sync.Request();
                    return Results.NoContent();
                })
                .WithTags("Cameras");
        }
    }
}
