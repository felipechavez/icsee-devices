using System.Security.Claims;
using CamGuard.Api.Common.Auth;
using CamGuard.Api.Common.Endpoints;

namespace CamGuard.Api.Features.Account;

/// <summary>
/// Usuario de la sesión y si es administrador. La web lo usa para mostrar u ocultar acciones;
/// la protección real está en <see cref="AuthSetup.AdminPolicy"/>.
/// </summary>
public static class GetMe
{
    public record Response(string Username, bool IsAdmin);

    public static Response ToResponse(ClaimsPrincipal user) => new(
        user.FindFirstValue("preferred_username") ?? user.FindFirstValue("sub") ?? "",
        AuthSetup.HasRealmRole(user, AuthSetup.AdminRole));

    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(ToResponse(user)))
                .WithTags("Account");
        }
    }
}
