using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Events;
using CamGuard.Api.Common.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace CamGuard.Api.Features.Monitoring;

/// <summary>
/// Difunde un cambio despierta/dormida: estado en memoria, Capture y Devices por sus canales,
/// la web por SignalR y el registro en BD. Lo usan el sondeo TCP (iCSee) y los eventos de Tuya.
/// </summary>
public class CameraStatePublisher(
    IServiceScopeFactory scopeFactory,
    CameraStateStore states,
    CameraEventChannel events,
    CameraAwakeChannel awakeEvents,
    IHubContext<EventsHub, IEventsClient> hub,
    TimeProvider clock,
    ILogger<CameraStatePublisher> logger)
{
    public async Task PublishAsync(Camera camera, CameraEventType type, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        logger.LogInformation("Cámara {Name}: {Type}", camera.Name, type);

        states.Set(camera.Id, type == CameraEventType.Awake, now);
        await events.Writer.WriteAsync(new CameraStateChanged(camera.Id, type, now), ct);
        if (type == CameraEventType.Awake) await awakeEvents.Writer.WriteAsync(camera.Id, ct);

        var message = new CameraStatusMessage(camera.Id, now);
        await (type == CameraEventType.Awake ? hub.Clients.All.CameraAwake(message) : hub.Clients.All.CameraAsleep(message));

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CameraEvents.Add(new CameraEvent { CameraId = camera.Id, Type = type, OccurredAt = now });
        await db.SaveChangesAsync(ct);
    }
}
