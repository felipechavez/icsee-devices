using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Streams;

namespace CamGuard.Tests.Features.Streams;

public class Go2rtcStreamSourceTests
{
    [Test]
    public async Task For_PlainCredentials_BuildsDvripUrl()
    {
        var source = Go2rtcStreamSource.For("192.168.1.86", 34567, "admin", "clave123");

        await Assert.That(source).IsEqualTo("dvrip://admin:clave123@192.168.1.86:34567?channel=0&subtype=0");
    }

    [Test]
    public async Task For_SpecialCharactersInPassword_AreEscaped()
    {
        var source = Go2rtcStreamSource.For("192.168.1.86", 34567, "admin", "p@ss:w/rd#1&x");

        await Assert.That(source).IsEqualTo("dvrip://admin:p%40ss%3Aw%2Frd%231%26x@192.168.1.86:34567?channel=0&subtype=0");
    }

    [Test]
    public async Task For_Ipv6_IsBracketed()
    {
        var source = Go2rtcStreamSource.For("fe80::1", 34567, "admin", "x");

        await Assert.That(source).IsEqualTo("dvrip://admin:x@[fe80::1]:34567?channel=0&subtype=0");
    }

    [Test]
    public async Task ForRtsp_AddsEscapedCredentialsAndKeepsQuery()
    {
        var source = Go2rtcStreamSource.ForRtsp("rtsp://192.168.1.102:554/cam/realmonitor?channel=1&subtype=0", "192.168.1.102", "admin", "p@ss:w/rd");

        await Assert.That(source).IsEqualTo("rtsp://admin:p%40ss%3Aw%2Frd@192.168.1.102:554/cam/realmonitor?channel=1&subtype=0");
    }

    [Test]
    public async Task ForRtsp_ForcesConfiguredHost()
    {
        var source = Go2rtcStreamSource.ForRtsp("rtsp://10.0.0.5:8554/live", "192.168.1.102", "admin", "x");

        await Assert.That(source).IsEqualTo("rtsp://admin:x@192.168.1.102:8554/live");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("http://192.168.1.102/onvif")]
    public async Task ForRtsp_NotRtsp_IsNull(string? uri)
    {
        await Assert.That(Go2rtcStreamSource.ForRtsp(uri, "192.168.1.102", "admin", "x")).IsNull();
    }

    [Test]
    [Arguments(CameraKind.Onvif, "imou_sub")]
    [Arguments(CameraKind.Icsee, "imou")]
    [Arguments(CameraKind.Tuya, "imou")]
    public async Task LiveName_OnlyOnvifUsesSubstream(CameraKind kind, string expected)
    {
        var camera = new Camera { Name = "x", Ip = "", Go2rtcStream = "imou", Kind = kind };

        await Assert.That(Go2rtcStreamNames.Live(camera)).IsEqualTo(expected);
    }
}
