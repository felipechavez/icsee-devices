using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CamGuard.Api.Features.Tuya;

namespace CamGuard.Tests.Features.Tuya;

public class TuyaMessageDecoderTests
{
    private const string Secret = "abcdefgh0123456789ABCDEFxyz";

    private const string Report = """
        {"dataId":"d1","devId":"eb8d8e837c0a688a86f1kb","productKey":"gmjdlzmivvtubrwj",
         "status":[{"code":"doorbell","t":1760321000000,"value":true},{"code":"residual_electricity","t":1760321000,"value":48}]}
        """;

    [Test]
    public async Task Decode_EcbStatusReport_ReturnsDevIdAndStatus()
    {
        var report = TuyaMessageDecoder.Decode(Message(EncryptEcb(Report), protocol: 4), Secret);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!.DevId).IsEqualTo("eb8d8e837c0a688a86f1kb");
        await Assert.That(report.Status.Select(s => s.Code)).IsEquivalentTo(["doorbell", "residual_electricity"]);
        await Assert.That((bool)report.Status[0].Value!).IsTrue();
    }

    [Test]
    public async Task Decode_GcmStatusReport_ReturnsStatus()
    {
        var report = TuyaMessageDecoder.Decode(Message(EncryptGcm(Report), protocol: 4, em: "aes_gcm"), Secret);

        await Assert.That(report?.Status.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Decode_GcmWithoutEmProperty_FallsBackToGcm()
    {
        var report = TuyaMessageDecoder.Decode(Message(EncryptGcm(Report), protocol: 4), Secret);

        await Assert.That(report?.Status.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Decode_GcmWithTimestampAsAad_ReturnsStatus()
    {
        var report = TuyaMessageDecoder.Decode(Message(EncryptGcm(Report, aad: "1760321000000"), protocol: 4, em: "aes_gcm"), Secret);

        await Assert.That(report?.Status.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Open_UndecryptableData_ReportsProtocolAndError()
    {
        var envelope = TuyaMessageDecoder.Open(Message(Convert.ToBase64String(new byte[40]), protocol: 4, em: "aes_gcm"), Secret);

        await Assert.That(envelope.Protocol).IsEqualTo(4);
        await Assert.That(envelope.Em).IsEqualTo("aes_gcm");
        await Assert.That(envelope.Report).IsNull();
        await Assert.That(envelope.Error).IsNotNull();
    }

    [Test]
    public async Task Decode_TimeInMillisecondsOrSeconds_IsNormalized()
    {
        var report = TuyaMessageDecoder.Decode(Message(EncryptEcb(Report), protocol: 4), Secret)!;

        await Assert.That(report.Status[0].At).IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1760321000000));
        await Assert.That(report.Status[1].At).IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(1760321000));
    }

    [Test]
    public async Task Decode_DevicePropertyMessage_ReturnsProperties()
    {
        const string body = """
            {"bizCode":"devicePropertyMessage","bizData":{"devId":"eb8d8e837c0a688a86f1kb","productId":"p",
             "properties":[{"code":"doorbell","value":true,"time":1760321000000,"dpId":19}]},"ts":1760321000000}
            """;

        var report = TuyaMessageDecoder.Decode(Message(EncryptGcm(body), protocol: 4, em: "aes_gcm"), Secret)!;

        await Assert.That(report.DevId).IsEqualTo("eb8d8e837c0a688a86f1kb");
        await Assert.That(report.Status[0].Code).IsEqualTo("doorbell");
        await Assert.That(report.Status[0].At).IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1760321000000));
    }

    [Test]
    public async Task Decode_OtherProtocol_ReturnsNull()
    {
        var report = TuyaMessageDecoder.Decode(Message(EncryptEcb("""{"bizCode":"online","devId":"x"}"""), protocol: 20), Secret);

        await Assert.That(report).IsNull();
    }

    [Test]
    public async Task Decode_InvalidPayload_ReturnsNull()
    {
        var message = new JsonObject { ["messageId"] = "m1", ["payload"] = "no-es-base64!!" };

        await Assert.That(TuyaMessageDecoder.Decode(message, Secret)).IsNull();
    }

    [Test]
    public async Task Decode_WrongKey_ReturnsNull()
    {
        var report = TuyaMessageDecoder.Decode(Message(EncryptEcb(Report), protocol: 4), "otraclave-0123456789ABCDEF");

        await Assert.That(report).IsNull();
    }

    [Test]
    public async Task MessageId_ReturnsId()
    {
        await Assert.That(TuyaMessageDecoder.MessageId(Message("x", 4))).IsEqualTo("m1");
    }

    [Test]
    public async Task MqPassword_IsMiddleOfDoubleMd5()
    {
        static string Md5(string s) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(s)));
        var expected = Md5("id" + Md5("secret"))[8..24];

        await Assert.That(TuyaMessageDecoder.MqPassword("id", "secret")).IsEqualTo(expected);
    }

    private static JsonObject Message(string encryptedData, int protocol, string? em = null)
    {
        var payload = new JsonObject { ["data"] = encryptedData, ["protocol"] = protocol, ["pv"] = "2.0", ["t"] = 1760321000000 };
        var message = new JsonObject
        {
            ["messageId"] = "m1",
            ["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToJsonString()))
        };
        if (em is not null) message["properties"] = new JsonObject { ["em"] = em };
        return message;
    }

    private static string EncryptEcb(string plain)
    {
        using var aes = Aes.Create();
        aes.Key = TuyaMessageDecoder.Key(Secret);
        return Convert.ToBase64String(aes.EncryptEcb(Encoding.UTF8.GetBytes(plain), PaddingMode.PKCS7));
    }

    private static string EncryptGcm(string plain, string? aad = null)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(TuyaMessageDecoder.Key(Secret), 16);
        gcm.Encrypt(nonce, data, cipher, tag, aad is null ? null : Encoding.UTF8.GetBytes(aad));
        return Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }
}
