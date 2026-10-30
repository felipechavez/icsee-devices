using CamGuard.Api.Common.Endpoints;

namespace CamGuard.Api.Features.Health;

public static class GetSystemStatus
{
    /// <summary>
    /// Devuelve la última revisión de <see cref="HealthMonitorService"/> (no consulta nada en vivo);
    /// 204 mientras no haya terminado la primera. No es <c>/health</c>, el health check anónimo de Docker.
    /// </summary>
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/status", (HealthSnapshotStore store) =>
                    store.Current is { } status ? Results.Ok(status) : Results.NoContent())
                .WithTags("Health");
        }
    }
}
