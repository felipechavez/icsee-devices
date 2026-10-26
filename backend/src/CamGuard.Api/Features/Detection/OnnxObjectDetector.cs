using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace CamGuard.Api.Features.Detection;

public interface IObjectDetector
{
    /// <summary>False si el modelo no existe o no se pudo cargar: los clips se suben sin analizar.</summary>
    bool IsAvailable { get; }

    /// <summary>Mejor confianza de cada clase COCO en un cuadro BGR de InputSize².</summary>
    IReadOnlyList<ObjectDetection> Detect(byte[] bgrFrame);
}

/// <summary>
/// YOLOX en ONNX Runtime (CPU; trae nativos para linux-x64 y linux-arm64). El modelo se carga la
/// primera vez que hace falta; si falla, se avisa una sola vez y queda no disponible.
/// </summary>
public sealed class OnnxObjectDetector(IOptions<DetectionOptions> options, ILogger<OnnxObjectDetector> logger) : IObjectDetector, IDisposable
{
    /// <summary>Lo que no llega a esto no se informa: ni siquiera sirve para calibrar.</summary>
    public const double ReportThreshold = 0.1;

    private readonly Lazy<InferenceSession?> _session = new(() => Load(options.Value, logger));

    public bool IsAvailable => _session.Value is not null;

    private static InferenceSession? Load(DetectionOptions o, ILogger logger)
    {
        if (!File.Exists(o.ModelPath))
        {
            logger.LogWarning("Modelo de IA no encontrado en {Path}: los clips se suben sin analizar", o.ModelPath);
            return null;
        }

        try
        {
            using var sessionOptions = new SessionOptions { IntraOpNumThreads = o.IntraOpThreads, InterOpNumThreads = 1 };
            var session = new InferenceSession(o.ModelPath, sessionOptions);
            logger.LogInformation("Modelo de IA cargado: {Path}", o.ModelPath);
            return session;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo cargar el modelo de IA {Path}: los clips se suben sin analizar", o.ModelPath);
            return null;
        }
    }

    public IReadOnlyList<ObjectDetection> Detect(byte[] bgrFrame)
    {
        var session = _session.Value ?? throw new InvalidOperationException("Modelo de IA no disponible");
        var size = options.Value.InputSize;

        var input = new DenseTensor<float>(ToChw(bgrFrame, size), [1, 3, size, size]);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        var output = results[0].AsTensor<float>();
        var (rows, rowLength) = (output.Dimensions[1], output.Dimensions[2]);
        var values = output is DenseTensor<float> dense ? dense.Buffer.Span : output.ToArray();
        return YoloxDecoder.Decode(values, rows, rowLength, ReportThreshold);
    }

    /// <summary>HWC de bytes → CHW de floats en 0–255, sin normalizar (YOLOX desde la 0.1.1).</summary>
    public static float[] ToChw(byte[] bgr, int size)
    {
        var plane = size * size;
        if (bgr.Length != plane * 3) throw new ArgumentException($"Se esperaba un cuadro de {size}×{size}×3 bytes.", nameof(bgr));

        var chw = new float[plane * 3];
        for (var i = 0; i < plane; i++)
        {
            chw[i] = bgr[i * 3];
            chw[plane + i] = bgr[i * 3 + 1];
            chw[2 * plane + i] = bgr[i * 3 + 2];
        }
        return chw;
    }

    public void Dispose()
    {
        if (_session.IsValueCreated) _session.Value?.Dispose();
    }
}
