namespace CamGuard.Api.Features.Detection;

public class DetectionOptions
{
    public const string Section = "Detection";

    /// <summary>Modelo YOLOX en ONNX (COCO). Si no existe, los clips se suben sin analizar (fail-open).</summary>
    public string ModelPath { get; set; } = "/app/models/yolox_nano.onnx";

    /// <summary>Lado de la entrada cuadrada del modelo: 416 en nano y tiny.</summary>
    public int InputSize { get; set; } = 416;

    /// <summary>Cuadros por segundo de video que se analizan.</summary>
    public double FramesPerSecond { get; set; } = 1;

    /// <summary>Tope de cuadros por clip; se corta antes en el primer positivo.</summary>
    public int MaxFrames { get; set; } = 20;

    /// <summary>Hilos de ONNX Runtime: en la Raspberry Pi deja núcleos libres a FFmpeg.</summary>
    public int IntraOpThreads { get; set; } = 2;
}
