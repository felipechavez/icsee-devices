using CamGuard.Api.Features.Capture;
using CamGuard.Api.Features.Detection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CamGuard.Tests.Features.Detection;

public class OnnxObjectDetectorTests
{
    [Test]
    public async Task ToChw_SplitsBgrPlanes()
    {
        // 2×2 píxeles: B = 1..4, G = 11..14, R = 21..24
        byte[] hwc = [1, 11, 21, 2, 12, 22, 3, 13, 23, 4, 14, 24];

        var chw = OnnxObjectDetector.ToChw(hwc, 2);

        await Assert.That(chw).IsEquivalentTo(new float[] { 1, 2, 3, 4, 11, 12, 13, 14, 21, 22, 23, 24 });
    }

    [Test]
    public async Task ToChw_WrongSize_Throws()
    {
        await Assert.That(() => OnnxObjectDetector.ToChw(new byte[10], 2)).Throws<ArgumentException>();
    }

    [Test]
    public async Task IsAvailable_MissingModel_False()
    {
        using var detector = new OnnxObjectDetector(
            Options.Create(new DetectionOptions { ModelPath = Path.Combine(Path.GetTempPath(), "no-existe.onnx") }),
            NullLogger<OnnxObjectDetector>.Instance);

        await Assert.That(detector.IsAvailable).IsFalse();
    }
}

/// <summary>
/// Modelo real con FFmpeg. Se salta si falta el modelo (CAMGUARD_DETECTION_MODEL o backend/models/yolox_nano.onnx)
/// o la foto de una persona (CAMGUARD_DETECTION_SAMPLE o backend/models/samples/person.jpg).
/// </summary>
[Category("Model")]
public class OnnxObjectDetectorModelTests
{
    private static string? Find(string envVar, string relative)
    {
        if (Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } fromEnv) return File.Exists(fromEnv) ? fromEnv : null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Test]
    public async Task Detect_PhotoOfPerson_Persona()
    {
        var model = Find("CAMGUARD_DETECTION_MODEL", Path.Combine("models", "yolox_nano.onnx"));
        var sample = Find("CAMGUARD_DETECTION_SAMPLE", Path.Combine("models", "samples", "person.jpg"));
        Skip.When(model is null || sample is null, "Falta el modelo o la foto de ejemplo");

        var options = Options.Create(new DetectionOptions { ModelPath = model! });
        using var detector = new OnnxObjectDetector(options, NullLogger<OnnxObjectDetector>.Instance);
        var sampler = new FrameSampler(Options.Create(new CaptureOptions()), options, NullLogger<FrameSampler>.Instance);

        var detections = new List<ObjectDetection>();
        await foreach (var frame in sampler.SampleAsync(sample!, CancellationToken.None))
            detections.AddRange(detector.Detect(frame));

        var verdict = DetectionPolicy.Evaluate(detections, people: true, animals: true, 0.5);
        await Assert.That(verdict.Label).IsEqualTo("persona");
    }
}
