using CamGuard.Api.Features.Capture;

namespace CamGuard.Tests.Features.Capture;

public class FfmpegRecorderTests
{
    [Test]
    public async Task RecordArguments_CopiesVideoWithoutReencoding()
    {
        var args = FfmpegRecorder.RecordArguments("rtsp://go2rtc:8554/icsee", "/data/clip.mp4", TimeSpan.FromSeconds(60));

        await Assert.That(string.Join(' ', args)).Contains("-c:v copy");
    }

    [Test]
    [Arguments(60, "60")]
    [Arguments(30.5, "30.5")]
    public async Task RecordArguments_UsesMaxDurationAsLimit(double seconds, string expected)
    {
        var args = FfmpegRecorder.RecordArguments("rtsp://x/y", "out.mp4", TimeSpan.FromSeconds(seconds)).ToList();

        await Assert.That(args[args.IndexOf("-t") + 1]).IsEqualTo(expected);
    }

    [Test]
    public async Task RecordArguments_InputAndOutputInRightPlaces()
    {
        var args = FfmpegRecorder.RecordArguments("rtsp://go2rtc:8554/icsee", "/data/clip.mp4", TimeSpan.FromSeconds(10)).ToList();

        await Assert.That(args[args.IndexOf("-i") + 1]).IsEqualTo("rtsp://go2rtc:8554/icsee");
        await Assert.That(args[^1]).IsEqualTo("/data/clip.mp4");
    }

    [Test]
    public async Task RecordArguments_UsesTcpTransport()
    {
        var args = FfmpegRecorder.RecordArguments("rtsp://x/y", "out.mp4", TimeSpan.FromSeconds(10)).ToList();

        await Assert.That(args[args.IndexOf("-rtsp_transport") + 1]).IsEqualTo("tcp");
    }

    [Test]
    public async Task ThumbnailArguments_ExtractsSingleFrameAtSeek()
    {
        var args = FfmpegRecorder.ThumbnailArguments("clip.mp4", "thumb.jpg", 1.0).ToList();

        await Assert.That(args[args.IndexOf("-ss") + 1]).IsEqualTo("1");
        await Assert.That(args[args.IndexOf("-frames:v") + 1]).IsEqualTo("1");
        await Assert.That(args[^1]).IsEqualTo("thumb.jpg");
    }
}
