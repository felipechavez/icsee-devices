namespace CamGuard.Api.Common.Storage;

/// <summary>
/// Archivos locales de una grabación en {DataDirectory}/clips: los escribe Capture, los lee Detection
/// y los borra Upload tras confirmar la subida (o Detection al descartar el clip).
/// </summary>
public static class ClipPaths
{
    public static (string Clip, string Thumbnail) For(string dataDirectory, Guid recordingId)
    {
        var clipsDir = Path.Combine(dataDirectory, "clips");
        Directory.CreateDirectory(clipsDir);
        return (Path.Combine(clipsDir, $"{recordingId:N}.mp4"), Path.Combine(clipsDir, $"{recordingId:N}.jpg"));
    }
}
