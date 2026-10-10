using System.Text.Json;
using CamGuard.Api.Common;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Upload;

public record UploadJob(Guid RecordingId, string ClipPath, string? ThumbnailPath, int Attempts = 0, DateTimeOffset? NextAttemptAt = null);

/// <summary>
/// Cola persistente: un JSON por trabajo en {DataDirectory}/queue. Sobrevive a
/// reinicios, y el clip local solo se borra tras confirmar la subida.
/// </summary>
public class UploadQueue
{
    private readonly string _directory;
    private readonly SemaphoreSlim _signal = new(0);

    public UploadQueue(IOptions<AppOptions> options)
    {
        _directory = Path.Combine(options.Value.DataDirectory, "queue");
        Directory.CreateDirectory(_directory);
    }

    public async Task EnqueueAsync(UploadJob job, CancellationToken ct)
    {
        await SaveAsync(job, ct);
        _signal.Release();
    }

    public async Task SaveAsync(UploadJob job, CancellationToken ct)
    {
        // Escribir a un temporal y renombrar: un corte de luz no deja JSON a medias.
        var path = PathFor(job.RecordingId);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(job), ct);
        File.Move(temp, path, overwrite: true);
    }

    public void Remove(Guid recordingId) => File.Delete(PathFor(recordingId));

    public async Task<IReadOnlyList<UploadJob>> GetDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var jobs = new List<UploadJob>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            var job = JsonSerializer.Deserialize<UploadJob>(await File.ReadAllTextAsync(file, ct));
            if (job is not null && (job.NextAttemptAt is null || job.NextAttemptAt <= now))
                jobs.Add(job);
        }

        return jobs.OrderBy(j => j.NextAttemptAt ?? DateTimeOffset.MinValue).ToList();
    }

    /// <summary>Espera un nuevo trabajo o hasta que pase el intervalo.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct) => await _signal.WaitAsync(timeout, ct);

    private string PathFor(Guid id) => Path.Combine(_directory, $"{id:N}.json");
}

public static class RetryPolicy
{
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(10);

    /// <summary>Backoff exponencial: 5 s, 10 s, 20 s… hasta 10 min. Nunca se rinde.</summary>
    public static TimeSpan Delay(int attempts)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 20);
        var delay = BaseDelay * Math.Pow(2, exponent);
        return delay > MaxDelay ? MaxDelay : delay;
    }
}
