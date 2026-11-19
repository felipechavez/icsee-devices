using CamGuard.Api.Common.Auth;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace CamGuard.Api.Features.Discovery;

/// <summary>"Buscar en la red" (solo administradores): cámaras iCSee, ONVIF y Tuya que aún no están en CamGuard.</summary>
public static class DiscoverCameras
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/cameras/discover", async (AppDbContext db, CameraDiscoverer discoverer, CancellationToken ct) =>
                {
                    var known = await db.Cameras.AsNoTracking().ToListAsync(ct);
                    var result = await discoverer.TryRunAsync(known, ct);
                    return result is null
                        ? Results.Problem("Ya hay una búsqueda en curso.", statusCode: StatusCodes.Status409Conflict)
                        : Results.Ok(result);
                })
                .RequireAuthorization(AuthSetup.AdminPolicy)
                .WithTags("Cameras");
        }
    }
}
