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

    /// <summary>Margen sobre el tope del clip antes de forzar el cierre de FFmpeg (iCSee; las Tuya usan Tuya:StartupGraceSeconds).</summary>
    public int StartupGraceSeconds { get; set; } = 30;
}

public record RecordResult(TimeSpan Elapsed, int ExitCode);

public interface IRecorder
{
    /// <param name="rotation">Giro a la derecha en grados; se guarda como metadato del MP4.</param>
    /// <param name="startupGrace">Margen sobre <paramref name="maxDuration"/> antes de forzar el cierre (espera del primer cuadro).</param>
    /// <param name="readTimeout">Tiempo sin datos tras el cual FFmpeg da el stream por cortado (también al arrancar).</param>
    /// <param name="stop">Al cancelarse, FFmpeg cierra el archivo limpiamente (cámara dormida).</param>
    Task<RecordResult> RecordAsync(string rtspUrl, string outputPath, TimeSpan maxDuration, int rotation, TimeSpan startupGrace,
        TimeSpan readTimeout, CancellationToken stop, CancellationToken ct);

    Task<bool> CreateThumbnailAsync(string videoPath, string thumbnailPath, CancellationToken ct);
}

public class FfmpegRecorder(IOptions<CaptureOptions> options, ILogger<FfmpegRecorder> logger) : IRecorder
{
    /// <summary>
    /// Copia el video sin recodificar. El audio de las iCSee suele ser G.711,
    /// que MP4 no admite, así que se convierte a AAC (costo mínimo).
    /// La rotación va como metadato (display matrix): no recodifica y el navegador la respeta.
    /// </summary>
    public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(10);

    public static IReadOnlyList<string> RecordArguments(string rtspUrl, string outputPath, TimeSpan maxDuration, int rotation = 0,
        TimeSpan? readTimeout = null) =>
    [
        "-hide_banner", "-loglevel", "error",
        "-rtsp_transport", "tcp",
        // Sin datos durante este tiempo = stream cortado (en microsegundos). Tuya necesita más: tarda en despertar.
        "-timeout", Microseconds(readTimeout ?? DefaultReadTimeout),
        // Con una espera larga también se analiza más: Tuya manda audio antes que el primer cuadro clave y su
        // SDP no trae los parámetros del video, así que el tamaño solo se conoce con ese cuadro.
        .. readTimeout > DefaultReadTimeout ? new[] { "-analyzeduration", Microseconds(readTimeout.Value) } : Array.Empty<string>(),
        // display_rotation es una opción de entrada y cuenta en sentido antihorario.
        .. rotation % 360 == 0 ? Array.Empty<string>() : new[] { "-display_rotation", (-rotation).ToString(CultureInfo.InvariantCulture) },
        "-i", rtspUrl,
        "-t", maxDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture),
        "-map", "0:v:0", "-map", "0:a:0?",
        "-c:v", "copy", "-c:a", "aac",
        "-movflags", "+faststart",
        "-y", outputPath
    ];

    private static string Microseconds(TimeSpan value) => ((long)value.TotalMicroseconds).ToString(CultureInfo.InvariantCulture);

    public static IReadOnlyList<string> ThumbnailArguments(string videoPath, string thumbnailPath, double seekSeconds) =>
    [
        "-hide_banner", "-loglevel", "error",
        "-ss", seekSeconds.ToString(CultureInfo.InvariantCulture),
        "-i", videoPath,
        "-frames:v", "1", "-vf", "scale=480:-2", "-q:v", "4",
        "-y", thumbnailPath
    ];

    public async Task<RecordResult> RecordAsync(string rtspUrl, string outputPath, TimeSpan maxDuration, int rotation, TimeSpan startupGrace,
        TimeSpan readTimeout, CancellationToken stop, CancellationToken ct)
    {
        // stdin redirigido: escribir 'q' hace que FFmpeg cierre el MP4 correctamente.
        using var process = Start(RecordArguments(rtspUrl, outputPath, maxDuration, rotation, readTimeout), redirectStdin: true);
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
        // Margen para el arranque y para que FFmpeg detecte por sí mismo un corte al final y cierre el MP4.
        var hardLimit = maxDuration + startupGrace + readTimeout;
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
