using System.Net;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace CamGuard.Api.Features.Cameras;

/// <summary>Datos editables de una cámara, compartidos por alta y edición.</summary>
/// <param name="Password">Obligatoria al crear; al editar, vacía o null conserva la actual.</param>
public record CameraInput(
    string Name,
    string Ip,
    int DvripPort,
    string Go2rtcStream,
    bool Enabled,
    string Username,
    string? Password = null,
    string? Model = null,
    MotionSensitivity MotionSensitivity = MotionSensitivity.Medium)
{
    public const int MaxPasswordLength = 128;

    public static Dictionary<string, string[]> Validate(CameraInput r, bool isNew)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100)
            errors[nameof(r.Name)] = ["El nombre es obligatorio (máx. 100 caracteres)."];
        if (!IPAddress.TryParse(r.Ip, out _))
            errors[nameof(r.Ip)] = ["IP inválida."];
        if (r.DvripPort is < 1 or > 65535)
            errors[nameof(r.DvripPort)] = ["Puerto fuera de rango."];
        if (string.IsNullOrWhiteSpace(r.Go2rtcStream) || r.Go2rtcStream.Length > 100
            || !r.Go2rtcStream.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
            errors[nameof(r.Go2rtcStream)] = ["El stream solo admite letras, números, '_' y '-'."];
        if (string.IsNullOrWhiteSpace(r.Username) || r.Username.Length > 64)
            errors[nameof(r.Username)] = ["El usuario es obligatorio (máx. 64 caracteres)."];
        if (isNew && string.IsNullOrEmpty(r.Password))
            errors[nameof(r.Password)] = ["La contraseña es obligatoria."];
        else if (r.Password?.Length > MaxPasswordLength)
            errors[nameof(r.Password)] = [$"La contraseña admite máx. {MaxPasswordLength} caracteres."];
        if (r.Model?.Length > 100)
            errors[nameof(r.Model)] = ["El modelo admite máx. 100 caracteres."];
        if (!Enum.IsDefined(r.MotionSensitivity))
            errors[nameof(r.MotionSensitivity)] = ["Sensibilidad inválida."];
        return errors;
    }

    /// <summary>Stream e IP+puerto no se pueden repetir entre cámaras.</summary>
    public async Task<Dictionary<string, string[]>> ValidateUniqueAsync(AppDbContext db, int? exceptId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var stream = Go2rtcStream.Trim();
        var ip = Ip.Trim();
        var others = db.Cameras.AsNoTracking().Where(c => exceptId == null || c.Id != exceptId);

        if (await others.AnyAsync(c => c.Go2rtcStream == stream, ct))
            errors[nameof(Go2rtcStream)] = ["Ya hay otra cámara con ese stream."];
        if (await others.AnyAsync(c => c.Ip == ip && c.DvripPort == DvripPort, ct))
            errors[nameof(Ip)] = ["Ya hay otra cámara con esa IP y puerto."];
        return errors;
    }

    public void ApplyTo(Camera camera, CameraSecretProtector secrets)
    {
        camera.Name = Name.Trim();
        camera.Ip = Ip.Trim();
        camera.DvripPort = DvripPort;
        camera.Go2rtcStream = Go2rtcStream.Trim();
        camera.Enabled = Enabled;
        camera.Username = Username.Trim();
        if (!string.IsNullOrEmpty(Password))
            camera.PasswordProtected = secrets.Protect(Password);
        camera.Model = string.IsNullOrWhiteSpace(Model) ? null : Model.Trim();
        // Solo se escribe en la cámara si cambió: así no se pisa lo configurado desde la app iCSee.
        if (camera.MotionSensitivity != MotionSensitivity)
        {
            camera.MotionSensitivity = MotionSensitivity;
            camera.SensitivityPending = true;
        }
    }
}
