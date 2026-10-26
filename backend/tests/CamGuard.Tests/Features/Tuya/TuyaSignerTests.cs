using System.Text;
using CamGuard.Api.Features.Tuya;

namespace CamGuard.Tests.Features.Tuya;

public class TuyaSignerTests
{
    private const string ClientId = "clientid123";
    private const string Secret = "secret0123456789abcdef";

    [Test]
    public async Task CanonicalUrl_SortsQueryByKey()
    {
        var url = TuyaSigner.CanonicalUrl("/v1.0/devices/x/logs", new Dictionary<string, string>
        {
            ["type"] = "7",
            ["end_time"] = "2",
            ["start_time"] = "1"
        });

        await Assert.That(url).IsEqualTo("/v1.0/devices/x/logs?end_time=2&start_time=1&type=7");
    }

    [Test]
    public async Task CanonicalUrl_NoQuery_ReturnsPath()
    {
        await Assert.That(TuyaSigner.CanonicalUrl("/v1.0/devices/x")).IsEqualTo("/v1.0/devices/x");
    }

    [Test]
    public async Task StringToSign_EmptyBody_UsesEmptySha256()
    {
        var sts = TuyaSigner.StringToSign("get", [], "/v1.0/token?grant_type=1");

        await Assert.That(sts).IsEqualTo("GET\ne3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n\n/v1.0/token?grant_type=1");
    }

    // Vectores calculados con poc/tuya/tuya-api.ps1, que ya funciona contra la API real.
    [Test]
    public async Task Sign_TokenRequest_MatchesPocScript()
    {
        var sts = TuyaSigner.StringToSign("GET", [], "/v1.0/token?grant_type=1");

        var sign = TuyaSigner.Sign(ClientId, Secret, "", "1700000000000", "nonce1", sts);

        await Assert.That(sign).IsEqualTo("59F70283D1912CDD5E085934F7D14D3677E37D1E8B8B6AB13EA7DE3E4641C81F");
    }

    [Test]
    public async Task Sign_BusinessRequestWithBody_MatchesPocScript()
    {
        var sts = TuyaSigner.StringToSign("POST", Encoding.UTF8.GetBytes("""{"type":"rtsp"}"""), "/v1.0/devices/abc/stream/actions/allocate");

        var sign = TuyaSigner.Sign(ClientId, Secret, "tok", "1700000000000", "nonce1", sts);

        await Assert.That(sign).IsEqualTo("80939E5FF81A6197BFB963892ECE27946C468DF666F0EC49EE5962B6D25393D5");
    }
}
