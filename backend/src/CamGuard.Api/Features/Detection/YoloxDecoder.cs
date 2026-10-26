namespace CamGuard.Api.Features.Detection;

public record ObjectDetection(int ClassId, double Score);

/// <summary>
/// Salida de YOLOX exportado sin decodificar: [1, N, 5 + clases], una fila por celda de las grillas con
/// strides 8, 16 y 32 (N = 3549 con entrada de 416). Cada fila trae caja (4), objectness y una
/// probabilidad por clase, ya con sigmoide. Solo importa si algo supera el umbral, así que no se
/// reconstruyen cajas ni hace falta NMS: devuelve la mejor confianza (obj × cls) de cada clase.
/// </summary>
public static class YoloxDecoder
{
    private const int BoxValues = 4;
    public static readonly int[] Strides = [8, 16, 32];

    /// <summary>Celdas de las tres grillas para una entrada cuadrada de <paramref name="inputSize"/>.</summary>
    public static int AnchorCount(int inputSize) => Strides.Sum(s => (inputSize / s) * (inputSize / s));

    /// <param name="output">Tensor aplanado de una imagen.</param>
    /// <param name="rows">N del tensor; debe coincidir con <see cref="AnchorCount"/>.</param>
    /// <param name="rowLength">5 + número de clases (85 en COCO).</param>
    /// <param name="minScore">Por debajo de esto la clase se ignora.</param>
    public static IReadOnlyList<ObjectDetection> Decode(ReadOnlySpan<float> output, int rows, int rowLength, double minScore)
    {
        var classes = rowLength - BoxValues - 1;
        if (classes <= 0 || output.Length < rows * rowLength)
            throw new ArgumentException($"Salida de YOLOX inesperada: {rows}×{rowLength} con {output.Length} valores.");

        var best = new double[classes];
        for (var i = 0; i < rows; i++)
        {
            var row = output.Slice(i * rowLength, rowLength);
            var objectness = row[BoxValues];
            if (objectness < minScore) continue;

            for (var c = 0; c < classes; c++)
            {
                var score = objectness * row[BoxValues + 1 + c];
                if (score > best[c]) best[c] = score;
            }
        }

        var detections = new List<ObjectDetection>();
        for (var c = 0; c < classes; c++)
        {
            if (best[c] >= minScore) detections.Add(new ObjectDetection(c, best[c]));
        }

        return detections;
    }
}
