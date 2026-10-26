using CamGuard.Api.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace CamGuard.Api.Common.Data;

/// <summary>
/// Cámara inicial que se crea si la tabla está vacía (variables Camera__*). Las demás
/// cámaras se agregan desde la web.
/// </summary>
public class CameraSeedOptions
{
    public const string Section = "Camera";

    public string Name { get; set; } = "iCSee";
    public string? Ip { get; set; }
    public int DvripPort { get; set; } = 34567;
    public string Go2rtcStream { get; set; } = "icsee";
    public string Username { get; set; } = "admin";
    public string? Password { get; set; }
}

public static class DatabaseInitializer
{
    /// <summary>
    /// Aplica migraciones y siembra la cámara. Reintenta porque en Docker
    /// Postgres tarda unos segundos en aceptar conexiones.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<CameraSecretProtector>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));
        var seed = scope.ServiceProvider.GetRequiredService<IConfiguration>()
            .GetSection(CameraSeedOptions.Section).Get<CameraSeedOptions>() ?? new CameraSeedOptions();

        const int maxAttempts = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("Base de datos no disponible (intento {Attempt}/{Max}): {Message}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        var password = string.IsNullOrEmpty(seed.Password) ? null : secrets.Protect(seed.Password);

        if (!string.IsNullOrWhiteSpace(seed.Ip) && !await db.Cameras.AnyAsync(ct))
        {
            db.Cameras.Add(new Camera
            {
                Name = seed.Name,
                Ip = seed.Ip,
                DvripPort = seed.DvripPort,
                Go2rtcStream = seed.Go2rtcStream,
                Username = seed.Username,
                PasswordProtected = password
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Cámara inicial creada: {Name} ({Ip})", seed.Name, seed.Ip);
            return;
        }

        // Instalaciones anteriores: la cámara sembrada no tenía credenciales en la base (vivían en go2rtc.yaml).
        if (password is not null)
        {
            var legacy = await db.Cameras.FirstOrDefaultAsync(c => c.Go2rtcStream == seed.Go2rtcStream && c.PasswordProtected == null, ct);
            if (legacy is not null)
            {
                legacy.Username = string.IsNullOrEmpty(legacy.Username) ? seed.Username : legacy.Username;
                legacy.PasswordProtected = password;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Credenciales de la cámara {Name} tomadas de Camera__*", legacy.Name);
            }
        }
    }
}
