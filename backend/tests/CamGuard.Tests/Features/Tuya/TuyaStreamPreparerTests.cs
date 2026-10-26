using CamGuard.Api.Features.Tuya;

namespace CamGuard.Tests.Features.Tuya;

public class TuyaStreamPreparerTests
{
    [Test]
    public async Task SourceFor_Direct_IsRtspsUrlWithLongTimeout()
    {
        await Assert.That(TuyaStreamPreparer.SourceFor("rtsps://h:443/v1/x?signInfo=a", viaFfmpeg: false, timeoutSeconds: 90))
            .IsEqualTo("rtsps://h:443/v1/x?signInfo=a#timeout=90");
    }

    [Test]
    public async Task SourceFor_ViaFfmpeg_CopiesVideoAndAudio()
    {
        await Assert.That(TuyaStreamPreparer.SourceFor("rtsps://h/x", viaFfmpeg: true, timeoutSeconds: 90))
            .IsEqualTo("ffmpeg:rtsps://h/x#video=copy#audio=copy");
    }
}
