using CamGuard.Api.Common.Data;

namespace CamGuard.Api.Features.Onvif;

/// <summary>
/// Convierte el movimiento que informa una cámara ONVIF en despierta/dormida, igual que el sondeo TCP
/// de las iCSee. Despierta con el primer "hay movimiento"; se duerme recién tras <c>asleepDelay</c> sin
/// movimiento, para no cortar el clip si la cámara alterna true/false por un instante.
/// </summary>
public class OnvifMotionTracker(TimeSpan asleepDelay)
{
    private DateTimeOffset? _quietSince;

    public bool IsAwake { get; private set; }

    /// <returns>El evento si hubo transición; null si no.</returns>
    public CameraEventType? Update(bool motion, DateTimeOffset now)
    {
        if (motion)
        {
            _quietSince = null;
            if (IsAwake) return null;
            IsAwake = true;
            return CameraEventType.Awake;
        }

        if (!IsAwake) return null;
        _quietSince ??= now;
        return Tick(now);
    }

    /// <summary>Se llama periódicamente: duerme la cámara cuando venció el retardo sin movimiento.</summary>
    public CameraEventType? Tick(DateTimeOffset now)
    {
        if (!IsAwake || _quietSince is not { } since || now - since < asleepDelay) return null;
        IsAwake = false;
        _quietSince = null;
        return CameraEventType.Asleep;
    }

    /// <summary>Cuánto falta para dormirla; null si no hay un cierre pendiente.</summary>
    public TimeSpan? TimeUntilAsleep(DateTimeOffset now)
    {
        if (!IsAwake || _quietSince is not { } since) return null;
        var left = asleepDelay - (now - since);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>Se perdió la conexión: sin eventos no se sabe cuándo termina el movimiento, así que se cierra.</summary>
    public CameraEventType? Disconnect()
    {
        _quietSince = null;
        if (!IsAwake) return null;
        IsAwake = false;
        return CameraEventType.Asleep;
    }
}
