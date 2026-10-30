namespace CamGuard.Api.Features.Upload;

/// <param name="FailingSince">Primer fallo desde la última subida exitosa; null = la última salió bien.</param>
public record UploadResult(DateTimeOffset? LastSuccessAt, DateTimeOffset? FailingSince, string? LastError);

/// <summary>Resultado de las subidas a R2, leído por Features/Health.</summary>
public class UploadHealth(TimeProvider clock)
{
    private UploadResult _current = new(null, null, null);

    public UploadResult Current => Volatile.Read(ref _current);

    public void Succeeded() => Volatile.Write(ref _current, new UploadResult(clock.GetUtcNow(), null, null));

    public void Failed(string error)
    {
        var current = Current;
        Volatile.Write(ref _current, current with { FailingSince = current.FailingSince ?? clock.GetUtcNow(), LastError = error });
    }
}
