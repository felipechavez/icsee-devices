using System.Security.Claims;
using System.Security.Cryptography;
using CamGuard.Api.Common.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CamGuard.Tests.Common.Auth;

public class AuthSetupTests
{
    private const string Authority = "https://camguard.example/auth/realms/camguard";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "kc" };

    private static AuthOptions Options() => new() { Authority = Authority, Audience = "camguard-api" };

    private static string Token(string issuer, string audience, DateTime expires)
    {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("preferred_username", "admin")]),
            IssuedAt = expires.AddMinutes(-10),
            NotBefore = expires.AddMinutes(-10),
            Expires = expires,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256)
        });
    }

    private static Task<TokenValidationResult> Validate(string token)
    {
        var parameters = AuthSetup.ValidationParameters(Options());
        parameters.IssuerSigningKey = Key;
        return new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
    }

    [Test]
    public async Task Validate_TokenFromAuthorityWithAudience_Valid()
    {
        var result = await Validate(Token(Authority, "camguard-api", DateTime.UtcNow.AddMinutes(5)));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Claims["preferred_username"]).IsEqualTo("admin");
    }

    [Test]
    [Arguments("account")]
    [Arguments("camguard-web")]
    public async Task Validate_WrongAudience_Invalid(string audience)
    {
        var result = await Validate(Token(Authority, audience, DateTime.UtcNow.AddMinutes(5)));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Validate_WrongIssuer_Invalid()
    {
        var result = await Validate(Token("https://camguard.example/auth/realms/master", "camguard-api", DateTime.UtcNow.AddMinutes(5)));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Validate_Expired_Invalid()
    {
        var result = await Validate(Token(Authority, "camguard-api", DateTime.UtcNow.AddMinutes(-5)));

        await Assert.That(result.IsValid).IsFalse();
    }

    private static ClaimsPrincipal User(params string[] realmAccess) =>
        new(new ClaimsIdentity(realmAccess.Select(v => new Claim(AuthSetup.RealmAccessClaim, v)), "test"));

    [Test]
    [Arguments("""{"roles":["offline_access","camguard-admin"]}""", true)]
    [Arguments("""{"roles":["offline_access","uma_authorization"]}""", false)]
    [Arguments("""{"roles":"camguard-admin"}""", false)]
    [Arguments("""{}""", false)]
    [Arguments("""["camguard-admin"]""", false)]
    [Arguments("no es json", false)]
    public async Task HasRealmRole_RealmAccessClaim_DetectsRole(string realmAccess, bool expected)
    {
        await Assert.That(AuthSetup.HasRealmRole(User(realmAccess), AuthSetup.AdminRole)).IsEqualTo(expected);
    }

    [Test]
    public async Task HasRealmRole_WithoutClaim_False()
    {
        await Assert.That(AuthSetup.HasRealmRole(User(), AuthSetup.AdminRole)).IsFalse();
    }

    [Test]
    public async Task HasRealmRole_RoleFromValidatedToken_True()
    {
        // Keycloak manda realm_access como objeto JSON: así lo entrega JsonWebTokenHandler al validar.
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = "camguard-api",
            Claims = new Dictionary<string, object>
            {
                [AuthSetup.RealmAccessClaim] = new Dictionary<string, object> { ["roles"] = new[] { "camguard-admin" } }
            },
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256)
        });

        var result = await Validate(token);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(AuthSetup.HasRealmRole(new ClaimsPrincipal(result.ClaimsIdentity), AuthSetup.AdminRole)).IsTrue();
    }

    [Test]
    [Arguments("""{"roles":["camguard-viewer"]}""", true)]
    [Arguments("""{"roles":["camguard-viewer","camguard-admin"]}""", false)]
    [Arguments("""{"roles":["camguard-admin"]}""", false)]
    [Arguments("""{"roles":["offline_access"]}""", false)]
    public async Task IsReadOnly_RealmRoles_ViewerWithoutAdmin(string realmAccess, bool expected)
    {
        await Assert.That(AuthSetup.IsReadOnly(User(realmAccess))).IsEqualTo(expected);
    }

    private static async Task<bool> Authorize(ClaimsPrincipal user, string? policyName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJwtAuth(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Authority"] = Authority })
            .Build());
        await using var provider = services.BuildServiceProvider();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = policyName is null ? await policies.GetFallbackPolicyAsync() : await policies.GetPolicyAsync(policyName);
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, policy!);
        return result.Succeeded;
    }

    [Test]
    [Arguments("""{"roles":["offline_access"]}""", true)]
    [Arguments("""{"roles":["camguard-admin"]}""", true)]
    [Arguments("""{"roles":["camguard-viewer","camguard-admin"]}""", true)]
    [Arguments("""{"roles":["camguard-viewer"]}""", false)]
    public async Task FallbackPolicy_RealmRoles_ExcludesReadOnly(string realmAccess, bool expected)
    {
        await Assert.That(await Authorize(User(realmAccess), null)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"roles":["camguard-viewer"]}""")]
    [Arguments("""{"roles":["offline_access"]}""")]
    public async Task ViewerPolicy_AuthenticatedUser_Allowed(string realmAccess)
    {
        await Assert.That(await Authorize(User(realmAccess), AuthSetup.ViewerPolicy)).IsTrue();
    }

    [Test]
    public async Task ViewerPolicy_Anonymous_Denied()
    {
        await Assert.That(await Authorize(new ClaimsPrincipal(new ClaimsIdentity()), AuthSetup.ViewerPolicy)).IsFalse();
    }

    [Test]
    public async Task AdminPolicy_Viewer_Denied()
    {
        await Assert.That(await Authorize(User("""{"roles":["camguard-viewer"]}"""), AuthSetup.AdminPolicy)).IsFalse();
    }

    [Test]
    public async Task ValidationParameters_AuthorityWithTrailingSlash_IssuerTrimmed()
    {
        var options = Options();
        options.Authority = Authority + "/";

        await Assert.That(AuthSetup.ValidationParameters(options).ValidIssuer).IsEqualTo(Authority);
    }
}
