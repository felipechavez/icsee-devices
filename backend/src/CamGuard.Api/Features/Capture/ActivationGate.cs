namespace CamGuard.Api.Features.Capture;

/// <summary>
/// Decide si una activación puede empezar a grabar: una sola grabación por
/// cámara a la vez y una pausa de enfriamiento después de cada una.
/// </summary>
public class ActivationGate
{
    private readonly Dictionary<int, (bool Recording, DateTimeOffset? FinishedAt)> _cameras = new();

    public bool TryStart(int cameraId, DateTimeOffset now, TimeSpan cooldown)
    {
        lock (_cameras)
        {
            var (recording, finishedAt) = _cameras.GetValueOrDefault(cameraId);
            if (recording) return false;
            if (finishedAt is { } f && now < f + cooldown) return false;

            _cameras[cameraId] = (true, finishedAt);
            return true;
        }
    }

    public void Finish(int cameraId, DateTimeOffset now)
    {
        lock (_cameras)
        {
            _cameras[cameraId] = (false, now);
        }
    }

    public bool IsRecording(int cameraId)
    {
        lock (_cameras)
        {
            return _cameras.GetValueOrDefault(cameraId).Recording;
        }
    }
}
