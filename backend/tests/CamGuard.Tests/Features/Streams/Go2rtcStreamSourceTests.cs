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
}
