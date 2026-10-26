using CamGuard.Api.Features.Detection;

namespace CamGuard.Tests.Features.Detection;

public class YoloxDecoderTests
{
    private const int RowLength = 5 + CocoClasses.Count;
    private static readonly int Rows = YoloxDecoder.AnchorCount(416);

    private static float[] Tensor(params (int Row, float Objectness, int ClassId, float ClassScore)[] objects)
    {
        var output = new float[Rows * RowLength];
        foreach (var (row, objectness, classId, classScore) in objects)
        {
            output[row * RowLength + 4] = objectness;
            output[row * RowLength + 5 + classId] = classScore;
        }
        return output;
    }

    [Test]
    public async Task AnchorCount_Input416_ThreeGrids()
    {
        // 52² + 26² + 13² celdas (strides 8, 16 y 32).
        await Assert.That(YoloxDecoder.AnchorCount(416)).IsEqualTo(3549);
    }

    [Test]
    public async Task Decode_ObjectInKnownCell_ReturnsClassAndScore()
    {
        var output = Tensor((Row: 2800, Objectness: 0.9f, ClassId: 16, ClassScore: 0.8f));

        var detections = YoloxDecoder.Decode(output, Rows, RowLength, 0.1);

        await Assert.That(detections).HasCount(1);
        await Assert.That(detections[0].ClassId).IsEqualTo(16);
        await Assert.That(detections[0].Score).IsEqualTo(0.72).Within(0.0001);
    }

    [Test]
    public async Task Decode_SameClassInSeveralCells_KeepsBest()
    {
        var output = Tensor((10, 0.9f, 0, 0.5f), (3000, 0.95f, 0, 0.9f));

        var detections = YoloxDecoder.Decode(output, Rows, RowLength, 0.1);

        await Assert.That(detections).HasCount(1);
        await Assert.That(detections[0].Score).IsEqualTo(0.855).Within(0.0001);
    }

    [Test]
    public async Task Decode_BelowThreshold_Ignored()
    {
        // 0.3 × 0.5 = 0.15 < 0.2
        var output = Tensor((100, 0.3f, 0, 0.5f));

        await Assert.That(YoloxDecoder.Decode(output, Rows, RowLength, 0.2)).IsEmpty();
    }

    [Test]
    public async Task Decode_TensorTooShort_Throws()
    {
        await Assert.That(() => YoloxDecoder.Decode(new float[10], Rows, RowLength, 0.1)).Throws<ArgumentException>();
    }
}
