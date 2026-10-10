using System.Collections.Concurrent;

namespace CamGuard.Api.Features.Monitoring;

public record CameraState(bool IsAwake, DateTimeOffset? LastChangedAt);

/// <summary>Último estado conocido de cada cámara, leído por los endpoints.</summary>
public class CameraStateStore
{
    private readonly ConcurrentDictionary<int, CameraState> _states = new();

    public CameraState Get(int cameraId) => _states.GetValueOrDefault(cameraId, new CameraState(false, null));

    public void Set(int cameraId, bool isAwake, DateTimeOffset at) => _states[cameraId] = new CameraState(isAwake, at);

    public void Remove(int cameraId) => _states.TryRemove(cameraId, out _);
}
