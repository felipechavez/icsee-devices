using CamGuard.Api.Common.Data;

namespace CamGuard.Api.Features.Monitoring;

/// <summary>
/// Convierte los sondeos TCP de una cámara en transiciones despierta/dormida.
/// Despierta con un sondeo exitoso; se considera dormida tras N fallos seguidos,
/// para no cortar una grabación por un sondeo perdido.
/// </summary>
public class CameraStateTracker(int asleepAfterFailures)
{
    private int _consecutiveFailures;

    public bool IsAwake { get; private set; }

    /// <returns>El tipo de evento si hubo transición; null si el estado no cambió.</returns>
    public CameraEventType? Update(bool portOpen)
    {
        if (portOpen)
        {
            _consecutiveFailures = 0;
            if (IsAwake) return null;
            IsAwake = true;
            return CameraEventType.Awake;
        }

        if (!IsAwake) return null;

        _consecutiveFailures++;
        if (_consecutiveFailures < Math.Max(1, asleepAfterFailures)) return null;

        IsAwake = false;
        _consecutiveFailures = 0;
        return CameraEventType.Asleep;
    }
}
