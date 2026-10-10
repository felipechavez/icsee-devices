using CamGuard.Api.Common;
using CamGuard.Api.Common.Storage;

namespace CamGuard.Tests.Common;

public class StorageKeysTests
{
    private static readonly Guid Id = Guid.Parse("0192f0a1-0000-7000-8000-000000000001");

    [Test]
    public async Task Clip_GroupsByCameraAndUtcDay()
    {
        // 23:30 en UTC-5 ya es el día siguiente en UTC.
        var startedAt = new DateTimeOffset(2026, 10, 4, 23, 30, 0, TimeSpan.FromHours(-5));

        var key = StorageKeys.Clip(3, Id, startedAt);

        await Assert.That(key).IsEqualTo("3/2026/10/05/0192f0a1000070008000000000000001.mp4");
    }

    [Test]
    public async Task Thumbnail_SharesPrefixWithClip()
    {
        var startedAt = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        var clip = StorageKeys.Clip(1, Id, startedAt);
        var thumb = StorageKeys.Thumbnail(1, Id, startedAt);

        await Assert.That(Path.ChangeExtension(clip, ".jpg")).IsEqualTo(thumb);
    }
}

public class Go2rtcOptionsTests
{
    [Test]
    [Arguments("rtsp://go2rtc:8554", "rtsp://go2rtc:8554/icsee")]
    [Arguments("rtsp://go2rtc:8554/", "rtsp://go2rtc:8554/icsee")]
    public async Task RtspUrlFor_JoinsBaseAndStream(string baseUrl, string expected)
    {
        var options = new Go2rtcOptions { RtspBaseUrl = baseUrl };

        await Assert.That(options.RtspUrlFor("icsee")).IsEqualTo(expected);
    }
}
