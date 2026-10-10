using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Capture;

public class CaptureOptions
{
    public const string Section = "Capture";

    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Tamaño mínimo para considerar que el clip tiene video.</summary>
    public long MinClipBytes { get; set; } = 10 * 1024;
}

public record RecordResult(TimeSpan Elapsed, int ExitCode);

public interface IRecorder
{
    /// <param name="stop">Al cancelarse, FFmpeg cierra el archivo limpiamente (cámara dormida).</param>
    Task<RecordResult> RecordAsync(string rtspUrl, string outputPath, TimeSpan maxDuration, CancellationToken stop, CancellationToken ct);

    Task<bool> CreateThumbnailAsync(string videoPath, string thumbnailPath, CancellationToken ct);
}

public class FfmpegRecorder(IOptions<CaptureOptions> options, ILogger<FfmpegRecorder> logger) : IRecorder
{
    /// <summary>
    /// Copia el video sin recodificar. El audio de las iCSee suele ser G.711,
    /// que MP4 no admite, así que se convierte a AAC (costo mínimo).
    /// </summary>
    public static IReadOnlyList<string> RecordArguments(string rtspUrl, string outputPath, TimeSpan maxDuration) =>
    [
        "-hide_banner", "-loglevel", "error",
        "-rtsp_transport", "tcp",
        "-timeout", "10000000", // 10 s sin datos = stream cortado
        "-i", rtspUrl,
        "-t", maxDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture),
        "-map", "0:v:0", "-map", "0:a:0?",
        "-c:v", "copy", "-c:a", "aac",
        "-movflags", "+faststart",
        "-y", outputPath
    ];

    public static IReadOnlyList<string> ThumbnailArguments(string videoPath, string thumbnailPath, double seekSeconds) =>
    [
        "-hide_banner", "-loglevel", "error",
        "-ss", seekSeconds.ToString(CultureInfo.InvariantCulture),
        "-i", videoPath,
        "-frames:v", "1", "-vf", "scale=480:-2", "-q:v", "4",
        "-y", thumbnailPath
    ];

    public async Task<RecordResult> RecordAsync(string rtspUrl, string outputPath, TimeSpan maxDuration, CancellationToken stop, CancellationToken ct)
    {
        // stdin redirigido: escribir 'q' hace que FFmpeg cierre el MP4 correctamente.
        using var process = Start(RecordArguments(rtspUrl, outputPath, maxDuration), redirectStdin: true);
        var stopwatch = Stopwatch.StartNew();

        await using var onStop = stop.Register(() =>
        {
            try
            {
                process.StandardInput.Write('q');
                process.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudo enviar 'q' a FFmpeg");
            }
        });

        var stderr = process.StandardError.ReadToEndAsync(ct);
        var hardLimit = maxDuration + TimeSpan.FromSeconds(30);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(hardLimit);

        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("FFmpeg no terminó a tiempo; se fuerza el cierre");
            process.Kill(entireProcessTree: true);
            if (ct.IsCancellationRequested) throw;
        }

        var errors = await stderr;
        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(errors))
        {
            logger.LogWarning("FFmpeg terminó con código {Code}: {Errors}", process.ExitCode, errors.Trim());
        }

        return new RecordResult(stopwatch.Elapsed, process.ExitCode);
    }

    public async Task<bool> CreateThumbnailAsync(string videoPath, string thumbnailPath, CancellationToken ct)
    {
        // Primero a 1 s (cuadro ya estabilizado); si el clip es más corto, el primer cuadro.
        foreach (var seek in new[] { 1.0, 0.0 })
        {
            using var process = Start(ThumbnailArguments(videoPath, thumbnailPath, seek), redirectStdin: false);
            await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (process.ExitCode == 0 && File.Exists(thumbnailPath) && new FileInfo(thumbnailPath).Length > 0)
                return true;
        }

        return false;
    }

    private Process Start(IEnumerable<string> args, bool redirectStdin)
    {
        var info = new ProcessStartInfo(options.Value.FfmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardInput = redirectStdin,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        return Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg");
    }
}
