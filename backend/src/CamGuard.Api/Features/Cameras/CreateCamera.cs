using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Common.Security;
using CamGuard.Api.Features.Monitoring;
using CamGuard.Api.Features.Streams;

namespace CamGuard.Api.Features.Cameras;

public static class CreateCamera
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/cameras", async (
                    CameraInput request,
                    AppDbContext db,
                    CameraSecretProtector secrets,
                    CameraStateStore states,
                    StreamSyncTrigger sync,
                    CancellationToken ct) =>
                {
                    var errors = CameraInput.Validate(request, isNew: true);
                    if (errors.Count > 0)
                        return Results.ValidationProblem(errors);

                    errors = await request.ValidateUniqueAsync(db, null, ct);
                    if (errors.Count > 0)
                        return Results.ValidationProblem(errors, statusCode: StatusCodes.Status409Conflict);

                    var camera = new Camera { Name = "", Ip = "", Go2rtcStream = "" };
                    request.ApplyTo(camera, secrets);
                    db.Cameras.Add(camera);
                    await db.SaveChangesAsync(ct);
                    sync.Request();

                    return Results.Created($"/api/cameras/{camera.Id}", ListCameras.ToResponse(camera, states.Get(camera.Id)));
                })
                .WithTags("Cameras");
        }
    }
}
