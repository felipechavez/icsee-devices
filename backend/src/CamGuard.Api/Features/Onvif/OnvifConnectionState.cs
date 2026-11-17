using System.Collections.Concurrent;

namespace CamGuard.Api.Features.Onvif;

public record OnvifConnection(bool Connected, DateTimeOffset Since, string? LastError);

/// <summary>
/// Conexión de eventos de cada cámara ONVIF (suscripción PullPoint activa o no), leída por Health y Cameras.
/// Sin entrada = todavía no se intentó (recién arrancó o la cámara está deshabilitada).
/// </summary>
public class OnvifConnectionState(TimeProvider clock)
{
    private readonly ConcurrentDictionary<int, OnvifConnection> _cameras = new();

    public OnvifConnection? Get(int cameraId) => _cameras.GetValueOrDefault(cameraId);

    public void Connected(int cameraId) =>
        _cameras.AddOrUpdate(cameraId,
            _ => new OnvifConnection(true, clock.GetUtcNow(), null),
            (_, current) => current.Connected ? current : new OnvifConnection(true, clock.GetUtcNow(), null));

    /// <summary>Conserva el "desde" mientras siga caída: los reintentos no reinician la cuenta.</summary>
    public void Disconnected(int cameraId, string error) =>
        _cameras.AddOrUpdate(cameraId,
            _ => new OnvifConnection(false, clock.GetUtcNow(), error),
            (_, current) => new OnvifConnection(false, current.Connected ? clock.GetUtcNow() : current.Since, error));

    public void Remove(int cameraId) => _cameras.TryRemove(cameraId, out _);
}
