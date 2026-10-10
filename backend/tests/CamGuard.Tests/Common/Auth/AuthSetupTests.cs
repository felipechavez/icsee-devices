using System.Security.Claims;
using System.Security.Cryptography;
using CamGuard.Api.Common.Auth;
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

    [Test]
    public async Task ValidationParameters_AuthorityWithTrailingSlash_IssuerTrimmed()
    {
        var options = Options();
        options.Authority = Authority + "/";

        await Assert.That(AuthSetup.ValidationParameters(options).ValidIssuer).IsEqualTo(Authority);
    }
}
