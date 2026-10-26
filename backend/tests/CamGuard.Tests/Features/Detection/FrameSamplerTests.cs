using System.Globalization;
using CamGuard.Api.Features.Detection;

namespace CamGuard.Tests.Features.Detection;

public class FrameSamplerTests
{
    [Test]
    public async Task SampleArguments_ScalesPadsAndLimitsFrames()
    {
        var args = FrameSampler.SampleArguments("/data/clips/a.mp4", 416, 1, 20).ToList();

        await Assert.That(args[args.IndexOf("-i") + 1]).IsEqualTo("/data/clips/a.mp4");
        await Assert.That(args[args.IndexOf("-vf") + 1])
            .IsEqualTo("fps=1:eof_action=pass,scale=416:416:force_original_aspect_ratio=decrease,pad=416:416:-1:-1:color=0x727272");
        await Assert.That(args[args.IndexOf("-frames:v") + 1]).IsEqualTo("20");
    }

    [Test]
    public async Task SampleArguments_RawBgrToStdout()
    {
        var args = FrameSampler.SampleArguments("a.mp4", 416, 1, 20).ToList();

        await Assert.That(args[args.IndexOf("-f") + 1]).IsEqualTo("rawvideo");
        await Assert.That(args[args.IndexOf("-pix_fmt") + 1]).IsEqualTo("bgr24");
        await Assert.That(args[^1]).IsEqualTo("pipe:1");
    }

    [Test]
    public async Task SampleArguments_FractionalFps_InvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-CL");
        try
        {
            var args = FrameSampler.SampleArguments("a.mp4", 416, 0.5, 20).ToList();

            await Assert.That(args[args.IndexOf("-vf") + 1]).StartsWith("fps=0.5:");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public async Task FrameBytes_SquareBgr()
    {
        await Assert.That(FrameSampler.FrameBytes(416)).IsEqualTo(416 * 416 * 3);
    }
}
