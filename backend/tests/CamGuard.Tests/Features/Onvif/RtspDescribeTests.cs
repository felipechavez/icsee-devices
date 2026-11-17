using System.Security.Cryptography;
using System.Text;
using CamGuard.Api.Features.Onvif;

namespace CamGuard.Tests.Features.Onvif;

public class RtspDescribeTests
{
    // SDP real del stream principal de una Imou Cue 2 (recortado).
    private const string ImouMainSdp =
        "v=0\r\no=- 2229913047 2229913047 IN IP4 0.0.0.0\r\ns=Media Server\r\nt=0 0\r\na=control:*\r\n" +
        "m=video 0 RTP/AVP 98\r\na=control:trackID=0\r\na=framerate:30.000000\r\na=rtpmap:98 H265/90000\r\n" +
        "a=fmtp:98 profile-id=1\r\na=recvonly\r\n" +
        "m=audio 0 RTP/AVP 97\r\na=control:trackID=1\r\na=rtpmap:97 MPEG4-GENERIC/16000\r\na=recvonly\r\n";

    [Test]
    public async Task VideoCodec_ImouMainStream_IsH265()
    {
        await Assert.That(RtspDescribe.VideoCodec(ImouMainSdp)).IsEqualTo("H265");
    }

    [Test]
    public async Task VideoCodec_H264Substream_IsH264()
    {
        var sdp = ImouMainSdp.Replace("H265/90000", "H264/90000");

        await Assert.That(RtspDescribe.VideoCodec(sdp)).IsEqualTo("H264");
    }

    [Test]
    public async Task VideoCodec_AudioOnly_IsNull()
    {
        const string sdp = "v=0\r\nm=audio 0 RTP/AVP 97\r\na=rtpmap:97 MPEG4-GENERIC/16000\r\n";

        await Assert.That(RtspDescribe.VideoCodec(sdp)).IsNull();
    }

    [Test]
    public async Task Authorization_Digest_ComputesMd5Response()
    {
        const string challenge = "Digest realm=\"Login to CE20533EFBA0B3D5\",nonce=\"e1107e3f-8e54\"";
        const string uri = "rtsp://192.168.1.102:554/cam/realmonitor?channel=1&subtype=0";

        var header = RtspDescribe.Authorization(challenge, "DESCRIBE", uri, "admin", "clave");

        var ha1 = Md5("admin:Login to CE20533EFBA0B3D5:clave");
        var ha2 = Md5($"DESCRIBE:{uri}");
        await Assert.That(header).Contains($"response=\"{Md5($"{ha1}:e1107e3f-8e54:{ha2}")}\"");
        await Assert.That(header).Contains("username=\"admin\"");
        await Assert.That(header).DoesNotContain("clave");
    }

    [Test]
    public async Task Authorization_Basic_EncodesCredentials()
    {
        var header = RtspDescribe.Authorization("Basic realm=\"x\"", "DESCRIBE", "rtsp://x/", "admin", "clave");

        await Assert.That(header).IsEqualTo("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:clave")));
    }

    [Test]
    public async Task Authorization_UnknownScheme_IsNull()
    {
        await Assert.That(RtspDescribe.Authorization("Negotiate", "DESCRIBE", "rtsp://x/", "a", "b")).IsNull();
    }

    private static string Md5(string text) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));
}
