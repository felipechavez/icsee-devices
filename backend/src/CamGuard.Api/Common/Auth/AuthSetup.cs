using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CamGuard.Api.Common.Auth;

/// <summary>Validación de los access tokens que emite Keycloak (realm de CamGuard).</summary>
public class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>Issuer público del realm, p. ej. https://camguard.example/auth/realms/camguard.</summary>
    public string Authority { get; set; } = "";

    /// <summary>Discovery por la red interna (http://keycloak:8080/...); si está vacío se usa el de Authority.</summary>
    public string MetadataAddress { get; set; } = "";

    public string Audience { get; set; } = "camguard-api";

    public bool RequireHttpsMetadata { get; set; }
}

public static class AuthSetup
{
    /// <summary>Rol de realm de Keycloak que habilita lo administrativo (p. ej. buscar cámaras en la red).</summary>
    public const string AdminRole = "camguard-admin";

    /// <summary>Policy de los endpoints solo para administradores.</summary>
    public const string AdminPolicy = "Admin";

    /// <summary>Claim de Keycloak con los roles de realm: <c>{"roles":["camguard-admin", ...]}</c>.</summary>
    public const string RealmAccessClaim = "realm_access";

    /// <summary>
    /// ¿El token trae el rol de realm? Con <c>MapInboundClaims = false</c>, Keycloak entrega <c>realm_access</c>
    /// como un claim con JSON. Un JSON ilegible o sin "roles" cuenta como sin rol.
    /// </summary>
    public static bool HasRealmRole(ClaimsPrincipal user, string role)
    {
        foreach (var claim in user.FindAll(RealmAccessClaim))
        {
            try
            {
                using var doc = JsonDocument.Parse(claim.Value);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("roles", out var roles)
                    && roles.ValueKind == JsonValueKind.Array
                    && roles.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String && r.GetString() == role))
                    return true;
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    public static TokenValidationParameters ValidationParameters(AuthOptions options) => new()
    {
        ValidIssuer = options.Authority.TrimEnd('/'),
        ValidAudience = options.Audience,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        RequireSignedTokens = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            throw new InvalidOperationException("Auth:Authority es obligatorio (issuer del realm de Keycloak).");
        }

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.Authority = options.Authority;
                if (!string.IsNullOrWhiteSpace(options.MetadataAddress))
                {
                    jwt.MetadataAddress = options.MetadataAddress;
                }

                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.TokenValidationParameters = ValidationParameters(options);

                // WebSockets no permiten cabeceras: SignalR manda el token en la query.
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(AdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasRealmRole(context.User, AdminRole)));

        return services;
    }
}
