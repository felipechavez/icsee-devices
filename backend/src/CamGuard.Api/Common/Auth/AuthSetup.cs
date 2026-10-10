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
                .Build());

        return services;
    }
}
