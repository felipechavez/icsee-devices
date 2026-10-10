using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace CamGuard.Api.Common.Security;

/// <summary>
/// Cifra la contraseña DVRIP de cada cámara antes de guardarla en SQL Server.
/// Las llaves viven en {DataDirectory}/keys: si se pierden, hay que volver a escribir las contraseñas.
/// </summary>
public class CameraSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("CamGuard.CameraPassword");

    public string Protect(string password) => _protector.Protect(password);

    /// <returns>La contraseña, o null si no hay o no se puede descifrar (llaves perdidas).</returns>
    public string? TryUnprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword)) return null;
        try
        {
            return _protector.Unprotect(protectedPassword);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
