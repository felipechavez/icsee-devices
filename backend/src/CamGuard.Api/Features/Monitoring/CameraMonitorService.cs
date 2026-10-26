using System.Net.Sockets;
using CamGuard.Api.Common.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Monitoring;

public class MonitorOptions
{
    public const string Section = "Monitor";

    public int PollIntervalMs { get; set; } = 1000;
    public int ProbeTimeoutMs { get; set; } = 800;
    public int AsleepAfterFailures { get; set; } = 3;
    public int CameraRefreshSeconds { get; set; } = 15;
}

public interface IPortProbe
{
    Task<bool> IsOpenAsync(string host, int port, TimeSpan timeout, CancellationToken ct);
}

public class TcpPortProbe : IPortProbe
{
    public async Task<bool> IsOpenAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}

/// <summary>
/// Sondea cada segundo el puerto DVRIP (34567) de las iCSee habilitadas.
/// La cámara a batería solo lo abre cuando despierta por movimiento. Las Tuya avisan por Features/Tuya.
/// </summary>
public class CameraMonitorService(
    IServiceScopeFactory scopeFactory,
    IPortProbe probe,
    CameraStateStore states,
    CameraStatePublisher publisher,
    IOptions<MonitorOptions> options,
    TimeProvider clock,
    ILogger<CameraMonitorService> logger) : BackgroundService
{
    private readonly Dictionary<int, CameraStateTracker> _trackers = new();
    private List<Camera> _cameras = [];
    private DateTimeOffset _camerasLoadedAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(o.PollIntervalMs), clock);

        do
        {
            try
            {
                await RefreshCamerasAsync(o, stoppingToken);
                await Task.WhenAll(_cameras.Select(c => PollAsync(c, o, stoppingToken)));
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error en el ciclo de monitoreo");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshCamerasAsync(MonitorOptions o, CancellationToken ct)
    {
        if (clock.GetUtcNow() - _camerasLoadedAt < TimeSpan.FromSeconds(o.CameraRefreshSeconds)) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var previous = _cameras;
        _cameras = await db.Cameras.AsNoTracking().Where(c => c.Enabled && c.Kind == CameraKind.Icsee).ToListAsync(ct);
        _camerasLoadedAt = clock.GetUtcNow();

        // Cámaras borradas o deshabilitadas: se cierra su activación y, si vuelven, empiezan de cero.
        var active = _cameras.Select(c => c.Id).ToHashSet();
        foreach (var removed in previous.Where(c => !active.Contains(c.Id)))
        {
            lock (_trackers) _trackers.Remove(removed.Id);
            if (!states.Get(removed.Id).IsAwake) continue;
            try
            {
                await publisher.PublishAsync(removed, CameraEventType.Asleep, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Si la cámara se borró, el evento no se puede guardar; el resto ya se aplicó.
                logger.LogWarning("No se pudo registrar el cierre de la cámara {Name}: {Message}", removed.Name, ex.Message);
            }
        }
    }

    private async Task PollAsync(Camera camera, MonitorOptions o, CancellationToken ct)
    {
        var open = await probe.IsOpenAsync(camera.Ip, camera.DvripPort, TimeSpan.FromMilliseconds(o.ProbeTimeoutMs), ct);

        CameraEventType? change;
        lock (_trackers)
        {
            if (!_trackers.TryGetValue(camera.Id, out var tracker))
            {
                tracker = new CameraStateTracker(o.AsleepAfterFailures);
                _trackers[camera.Id] = tracker;
            }

            change = tracker.Update(open);
        }

        if (change is { } type)
        {
            await publisher.PublishAsync(camera, type, ct);
        }
    }
}

public static class MonitoringFeature
{
    public static IServiceCollection AddMonitoringFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MonitorOptions>(configuration.GetSection(MonitorOptions.Section));
        services.AddSingleton<CameraStateStore>();
        services.AddSingleton<CameraStatePublisher>();
        services.AddSingleton<IPortProbe, TcpPortProbe>();
        services.AddHostedService<CameraMonitorService>();
        return services;
    }
}
