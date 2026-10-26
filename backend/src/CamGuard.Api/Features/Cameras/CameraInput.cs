using System.Net;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace CamGuard.Api.Features.Cameras;

/// <summary>Datos editables de una cámara, compartidos por alta y edición.</summary>
/// <param name="Password">Obligatoria al crear una iCSee; al editar, vacía o null conserva la actual.</param>
/// <param name="Kind">Solo se elige en el alta; al editar debe coincidir con el guardado.</param>
public record CameraInput(
    string Name,
    string Ip,
    int DvripPort,
    string Go2rtcStream,
    bool Enabled,
    string Username,
    string? Password = null,
    string? Model = null,
    MotionSensitivity MotionSensitivity = MotionSensitivity.Medium,
    CameraKind Kind = CameraKind.Icsee,
    string? TuyaDeviceId = null,
    int Rotation = 0,
    string? TuyaTriggerDeviceIds = null,
    DetectionMode DetectionMode = DetectionMode.Tag,
    bool DetectPeople = true,
    bool DetectAnimals = true)
{
    public const int MaxPasswordLength = 128;
    public const int MaxTriggerDevices = 10;

    public static readonly int[] Rotations = [0, 90, 180, 270];

    public static Dictionary<string, string[]> Validate(CameraInput r, bool isNew)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100)
            errors[nameof(r.Name)] = ["El nombre es obligatorio (máx. 100 caracteres)."];
        if (string.IsNullOrWhiteSpace(r.Go2rtcStream) || r.Go2rtcStream.Length > 100
            || !r.Go2rtcStream.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
            errors[nameof(r.Go2rtcStream)] = ["El stream solo admite letras, números, '_' y '-'."];
        if (r.Model?.Length > 100)
            errors[nameof(r.Model)] = ["El modelo admite máx. 100 caracteres."];
        if (!Rotations.Contains(r.Rotation))
            errors[nameof(r.Rotation)] = ["La rotación debe ser 0, 90, 180 o 270."];
        if (!Enum.IsDefined(r.DetectionMode))
            errors[nameof(r.DetectionMode)] = ["Modo de detección inválido."];
        else if (r.DetectionMode == DetectionMode.Filter && !r.DetectPeople && !r.DetectAnimals)
            errors[nameof(r.DetectionMode)] = ["Para filtrar, activa personas, animales o ambos."];

        switch (r.Kind)
        {
            case CameraKind.Icsee:
                ValidateIcsee(r, isNew, errors);
                break;
            case CameraKind.Tuya:
                // Sin IP ni credenciales: todo pasa por la Tuya Cloud API con las claves del proyecto.
                var id = r.TuyaDeviceId?.Trim() ?? "";
                if (!IsDeviceId(id))
                    errors[nameof(r.TuyaDeviceId)] = ["El Device ID de Tuya es obligatorio (letras y números)."];
                var triggers = Camera.SplitDeviceIds(r.TuyaTriggerDeviceIds);
                if (triggers.Count > MaxTriggerDevices || triggers.Any(t => !IsDeviceId(t) || t == id))
                    errors[nameof(r.TuyaTriggerDeviceIds)] =
                        [$"Hasta {MaxTriggerDevices} Device IDs de sensores, separados por coma, distintos del de la cámara."];
                break;
            default:
                errors[nameof(r.Kind)] = ["Tipo de cámara inválido."];
                break;
        }

        return errors;
    }

    private static bool IsDeviceId(string id) => id.Length is >= 10 and <= 64 && id.All(char.IsAsciiLetterOrDigit);

    private static void ValidateIcsee(CameraInput r, bool isNew, Dictionary<string, string[]> errors)
    {
        if (!IPAddress.TryParse(r.Ip, out _))
            errors[nameof(r.Ip)] = ["IP inválida."];
        if (r.DvripPort is < 1 or > 65535)
            errors[nameof(r.DvripPort)] = ["Puerto fuera de rango."];
        if (string.IsNullOrWhiteSpace(r.Username) || r.Username.Length > 64)
            errors[nameof(r.Username)] = ["El usuario es obligatorio (máx. 64 caracteres)."];
        if (isNew && string.IsNullOrEmpty(r.Password))
            errors[nameof(r.Password)] = ["La contraseña es obligatoria."];
        else if (r.Password?.Length > MaxPasswordLength)
            errors[nameof(r.Password)] = [$"La contraseña admite máx. {MaxPasswordLength} caracteres."];
        if (!Enum.IsDefined(r.MotionSensitivity))
            errors[nameof(r.MotionSensitivity)] = ["Sensibilidad inválida."];
    }

    /// <summary>Al editar no se puede cambiar el tipo: los datos de uno no sirven para el otro.</summary>
    public static Dictionary<string, string[]> ValidateKindUnchanged(CameraInput r, Camera camera) =>
        r.Kind == camera.Kind
            ? []
            : new() { [nameof(r.Kind)] = ["El tipo de cámara no se puede cambiar; elimínala y agrégala de nuevo."] };

    /// <summary>Stream, IP+puerto (iCSee) y Device ID (Tuya) no se pueden repetir entre cámaras.</summary>
    public async Task<Dictionary<string, string[]>> ValidateUniqueAsync(AppDbContext db, int? exceptId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var stream = Go2rtcStream.Trim();
        var others = db.Cameras.AsNoTracking().Where(c => exceptId == null || c.Id != exceptId);

        if (await others.AnyAsync(c => c.Go2rtcStream == stream, ct))
            errors[nameof(Go2rtcStream)] = ["Ya hay otra cámara con ese stream."];

        if (Kind == CameraKind.Icsee)
        {
            var ip = Ip.Trim();
            if (await others.AnyAsync(c => c.Kind == CameraKind.Icsee && c.Ip == ip && c.DvripPort == DvripPort, ct))
                errors[nameof(Ip)] = ["Ya hay otra cámara con esa IP y puerto."];
        }
        else
        {
            var deviceId = TuyaDeviceId!.Trim();
            if (await others.AnyAsync(c => c.TuyaDeviceId == deviceId, ct))
                errors[nameof(TuyaDeviceId)] = ["Ya hay otra cámara con ese Device ID."];
        }

        return errors;
    }

    public void ApplyTo(Camera camera, CameraSecretProtector secrets)
    {
        camera.Name = Name.Trim();
        camera.Kind = Kind;
        camera.Go2rtcStream = Go2rtcStream.Trim();
        camera.Enabled = Enabled;
        camera.Model = string.IsNullOrWhiteSpace(Model) ? null : Model.Trim();
        camera.Rotation = Rotation;
        camera.DetectionMode = DetectionMode;
        camera.DetectPeople = DetectPeople;
        camera.DetectAnimals = DetectAnimals;

        if (Kind == CameraKind.Tuya)
        {
            camera.TuyaDeviceId = TuyaDeviceId!.Trim();
            var triggers = Camera.SplitDeviceIds(TuyaTriggerDeviceIds);
            camera.TuyaTriggerDeviceIds = triggers.Count == 0 ? null : string.Join(',', triggers);
            camera.Ip = "";
            return;
        }

        camera.TuyaDeviceId = null;
        camera.TuyaTriggerDeviceIds = null;
        camera.Ip = Ip.Trim();
        camera.DvripPort = DvripPort;
        camera.Username = Username.Trim();
        if (!string.IsNullOrEmpty(Password))
            camera.PasswordProtected = secrets.Protect(Password);
        // Solo se escribe en la cámara si cambió: así no se pisa lo configurado desde la app iCSee.
        if (camera.MotionSensitivity != MotionSensitivity)
        {
            camera.MotionSensitivity = MotionSensitivity;
            camera.SensitivityPending = true;
        }
    }
}
