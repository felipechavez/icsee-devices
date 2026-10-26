using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CamGuard.Api.Features.Capture;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Detection;

/// <summary>
/// Extrae cuadros de un clip con FFmpeg, ya listos para el modelo: BGR crudo de
/// <see cref="DetectionOptions.InputSize"/>², sin librería de imágenes.
/// </summary>
public class FrameSampler(IOptions<CaptureOptions> captureOptions, IOptions<DetectionOptions> options, ILogger<FrameSampler> logger)
{
    /// <summary>
    /// Escala sin deformar y rellena con gris 114 (0x72), en BGR, como se entrenó YOLOX. FFmpeg aplica al
    /// decodificar la rotación guardada como metadato, así que los cuadros llegan derechos.
    /// Sin eof_action=pass, fps descarta el único cuadro de un clip de menos de 1 s (o de una foto).
    /// </summary>
    public static IReadOnlyList<string> SampleArguments(string videoPath, int inputSize, double framesPerSecond, int maxFrames)
    {
        var size = inputSize.ToString(CultureInfo.InvariantCulture);
        var fps = framesPerSecond.ToString(CultureInfo.InvariantCulture);
        return
        [
            "-hide_banner", "-loglevel", "error",
            "-i", videoPath,
            "-an",
            "-vf", $"fps={fps}:eof_action=pass,scale={size}:{size}:force_original_aspect_ratio=decrease,pad={size}:{size}:-1:-1:color=0x727272",
            "-frames:v", maxFrames.ToString(CultureInfo.InvariantCulture),
            "-f", "rawvideo", "-pix_fmt", "bgr24",
            "pipe:1"
        ];
    }

    public static int FrameBytes(int inputSize) => inputSize * inputSize * 3;

    /// <summary>
    /// Cuadros BGR (alto × ancho × 3). Si el consumidor corta antes (primer positivo), FFmpeg se mata:
    /// es seguro porque solo lee. Falla si FFmpeg termina con error sin entregar ningún cuadro.
    /// </summary>
    public async IAsyncEnumerable<byte[]> SampleAsync(string videoPath, [EnumeratorCancellation] CancellationToken ct)
    {
        var o = options.Value;
        var info = new ProcessStartInfo(captureOptions.Value.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in SampleArguments(videoPath, o.InputSize, o.FramesPerSecond, o.MaxFrames)) info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg");
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(ct);
            var stdout = process.StandardOutput.BaseStream;
            var frameBytes = FrameBytes(o.InputSize);
            var frames = 0;

            while (true)
            {
                var frame = new byte[frameBytes];
                if (await stdout.ReadAtLeastAsync(frame, frameBytes, throwOnEndOfStream: false, ct) < frameBytes) break;
                frames++;
                yield return frame;
            }

            await process.WaitForExitAsync(ct);
            var errors = (await stderr).Trim();
            if (process.ExitCode != 0)
            {
                if (frames == 0) throw new InvalidOperationException($"FFmpeg terminó con código {process.ExitCode}: {errors}");
                logger.LogWarning("FFmpeg terminó con código {Code} tras {Frames} cuadros: {Errors}", process.ExitCode, frames, errors);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Terminó justo antes.
                }
            }
        }
    }
}
