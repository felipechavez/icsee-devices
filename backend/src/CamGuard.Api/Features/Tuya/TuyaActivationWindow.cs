namespace CamGuard.Api.Features.Tuya;

/// <summary>
/// Los dispositivos Tuya no avisan cuándo se duermen: una activación dura un tiempo fijo desde el
/// último evento que graba, y cada evento nuevo la extiende. Sin estado externo, para probarla sola.
/// </summary>
public class TuyaActivationWindow(TimeSpan duration)
{
    private readonly Dictionary<int, DateTimeOffset> _until = new();

    /// <returns>true si la activación empieza ahora (no había una en curso).</returns>
    public bool Trigger(int cameraId, DateTimeOffset now)
    {
        lock (_until)
        {
            var started = !_until.TryGetValue(cameraId, out var until) || until <= now;
            _until[cameraId] = now + duration;
            return started;
        }
    }

    public bool IsActive(int cameraId, DateTimeOffset now)
    {
        lock (_until) return _until.TryGetValue(cameraId, out var until) && until > now;
    }

    /// <summary>Quita y devuelve las cámaras cuya activación venció.</summary>
    public IReadOnlyList<int> Expire(DateTimeOffset now)
    {
        lock (_until)
        {
            var expired = _until.Where(u => u.Value <= now).Select(u => u.Key).ToList();
            foreach (var id in expired) _until.Remove(id);
            return expired;
        }
    }

    /// <summary>Cámara borrada o deshabilitada: se olvida sin publicar nada.</summary>
    public bool Remove(int cameraId)
    {
        lock (_until) return _until.Remove(cameraId);
    }
}
