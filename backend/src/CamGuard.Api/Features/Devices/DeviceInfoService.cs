using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Events;
using CamGuard.Api.Common.Realtime;
using CamGuard.Api.Common.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Devices;

public class DevicesOptions
{
    public const string Section = "Devices";

    /// <summary>Leer batería/almacenamiento y aplicar la sensibilidad cada vez que una cámara despierta.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Espera tras despertar, para no competir con go2rtc y FFmpeg al abrir el stream.</summary>
    public int QueryDelaySeconds { get; set; } = 3;

    public int TimeoutSeconds { get; set; } = 5;
}

/// <summary>Abre una sesión DVRIP con las credenciales guardadas de una cámara.</summary>
public class DvripConnector(CameraSecretProtector secrets, IOptions<DevicesOptions> options)
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(options.Value.TimeoutSeconds);

    public async Task<DvripClient> OpenAsync(string ip, int port, string username, string password, CancellationToken ct)
    {
        var client = await DvripClient.ConnectAsync(ip, port, Timeout, ct);
        try
        {
            await client.LoginAsync(username, password, ct);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public Task<DvripClient> OpenAsync(Camera camera, CancellationToken ct)
    {
        var password = secrets.TryUnprotect(camera.PasswordProtected)
            ?? throw new DvripException("La cámara no tiene contraseña guardada.");
        return OpenAsync(camera.Ip, camera.DvripPort, camera.Username, password, ct);
    }
}

/// <summary>
/// Al despertar una cámara lee modelo, batería y SD por DVRIP, aplica la sensibilidad pendiente
/// y avisa a la web. Nunca despierta una cámara: solo actúa cuando Monitoring avisa que despertó.
/// </summary>
public class DeviceInfoService(
    CameraAwakeChannel awakeEvents,
    DvripConnector connector,
    IServiceScopeFactory scopeFactory,
    IHubContext<EventsHub, IEventsClient> hub,
    IOptions<DevicesOptions> options,
    TimeProvider clock,
    ILogger<DeviceInfoService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<int, Task> _running = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var cameraId in awakeEvents.Reader.ReadAllAsync(stoppingToken))
            {
                if (!options.Value.Enabled || _running.ContainsKey(cameraId)) continue;
                _running[cameraId] = Task.Run(async () =>
                {
                    try
                    {
                        await RefreshAsync(cameraId, stoppingToken);
                    }
                    finally
                    {
                        _running.TryRemove(cameraId, out _);
                    }
                }, CancellationToken.None);
            }
        }
        finally
        {
            await Task.WhenAll(_running.Values);
        }
    }

    private async Task RefreshAsync(int cameraId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.Value.QueryDelaySeconds), clock, ct);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var camera = await db.Cameras.FindAsync([cameraId], ct);
            // Las Tuya no hablan DVRIP: su batería llega por Features/Tuya.
            if (camera is null || camera.Kind != CameraKind.Icsee || string.IsNullOrEmpty(camera.PasswordProtected)) return;

            await using var client = await connector.OpenAsync(camera, ct);
            var snapshot = await ReadSnapshotAsync(client, ct);

            if (string.IsNullOrWhiteSpace(camera.Model)) camera.Model = snapshot.Model;
            camera.BatteryPercent = snapshot.BatteryPercent ?? camera.BatteryPercent;
            camera.StorageUsedBytes = snapshot.StorageUsedBytes;
            camera.StorageTotalBytes = snapshot.StorageTotalBytes;
            camera.DeviceInfoAt = clock.GetUtcNow();

            if (camera.SensitivityPending && await ApplySensitivityAsync(client, camera.MotionSensitivity, ct))
                camera.SensitivityPending = false;

            await db.SaveChangesAsync(ct);
            await hub.Clients.All.CameraInfoUpdated(ToMessage(camera));
            logger.LogInformation("Cámara {Name}: batería {Battery}, SD {Used}/{Total}",
                camera.Name, camera.BatteryPercent, camera.StorageUsedBytes, camera.StorageTotalBytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Sin la información la grabación sigue igual: solo se avisa.
            logger.LogWarning("No se pudo leer el estado de la cámara {Id} por DVRIP: {Message}", cameraId, ex.Message);
        }
    }

    public static CameraInfoMessage ToMessage(Camera c) =>
        new(c.Id, c.Model, c.BatteryPercent, c.StorageUsedBytes, c.StorageTotalBytes, c.MotionSensitivity, c.SensitivityPending, c.DeviceInfoAt);

    public static async Task<DeviceSnapshot> ReadSnapshotAsync(DvripClient client, CancellationToken ct)
    {
        var model = await TryAsync(() => client.GetInfoAsync(DvripQueries.SystemInfo, ct)) is { } sys
            ? DeviceInfoParser.ParseModel(sys)
            : null;
        var storage = await TryAsync(() => client.GetInfoAsync(DvripQueries.StorageInfo, ct)) is { } st
            ? DeviceInfoParser.ParseStorage(st)
            : null;

        int? battery = null;
        foreach (var name in DvripQueries.Battery)
        {
            battery = DeviceInfoParser.ParseBattery(await TryAsync(() => client.GetInfoAsync(name, ct)))
                ?? DeviceInfoParser.ParseBattery(await TryAsync(() => client.GetConfigAsync(name, ct)));
            if (battery is not null) break;
        }

        return new DeviceSnapshot(battery, storage?.Used, storage?.Total, model);
    }

    /// <summary>Escribe el nivel en la primera configuración de detección que exista.</summary>
    public static async Task<bool> ApplySensitivityAsync(DvripClient client, MotionSensitivity sensitivity, CancellationToken ct)
    {
        foreach (var name in DvripQueries.Detection)
        {
            if (await TryAsync(() => client.GetConfigAsync(name, ct)) is not { } response) continue;
            if (SensitivityMap.WithLevel(response[name], SensitivityMap.ToLevel(sensitivity)) is not { } updated) continue;
            await client.SetConfigAsync(name, updated, ct);
            return true;
        }
        return false;
    }

    /// <summary>Una consulta que el firmware no soporta (Ret ≠ 100) no corta la sesión.</summary>
    private static async Task<JsonObject?> TryAsync(Func<Task<JsonObject>> query)
    {
        try
        {
            return await query();
        }
        catch (DvripException)
        {
            return null;
        }
    }
}

public static class DevicesFeature
{
    public static IServiceCollection AddDevicesFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DevicesOptions>(configuration.GetSection(DevicesOptions.Section));
        services.AddSingleton<DvripConnector>();
        services.AddHostedService<DeviceInfoService>();
        return services;
    }
}
