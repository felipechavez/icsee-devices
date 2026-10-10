using System.Text;
using System.Text.Json.Nodes;
using CamGuard.Api.Features.Devices;

namespace CamGuard.Tests.Features.Devices;

public class DvripClientTests
{
    [Test]
    public async Task SofiaHash_EmptyPassword_MatchesKnownValue()
    {
        // Valor conocido del firmware XM para la contraseña vacía.
        await Assert.That(DvripClient.SofiaHash("")).IsEqualTo("tlJwpbo6");
    }

    [Test]
    [Arguments("admin")]
    [Arguments("contraseña larga 123")]
    public async Task SofiaHash_AnyPassword_EightAlphanumericChars(string password)
    {
        var hash = DvripClient.SofiaHash(password);

        await Assert.That(hash.Length).IsEqualTo(8);
        await Assert.That(hash.All(char.IsAsciiLetterOrDigit)).IsTrue();
    }

    [Test]
    public async Task EncodeFrame_DecodeHeader_RoundTrip()
    {
        var frame = DvripClient.EncodeFrame(0x0B, 7, DvripClient.InfoRequest, """{"Name":"SystemInfo"}""");

        var (session, messageId, length) = DvripClient.DecodeHeader(frame);

        await Assert.That(frame[0]).IsEqualTo((byte)0xFF);
        await Assert.That(session).IsEqualTo(0x0Bu);
        await Assert.That(messageId).IsEqualTo(DvripClient.InfoRequest);
        await Assert.That(length).IsEqualTo(frame.Length - DvripClient.HeaderSize);
    }

    [Test]
    public async Task EncodeFrame_PayloadEndsWithNewlineAndZero()
    {
        var frame = DvripClient.EncodeFrame(0, 0, DvripClient.LoginRequest, "{}");

        await Assert.That(frame[^2]).IsEqualTo((byte)'\n');
        await Assert.That(frame[^1]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task DecodeHeader_WrongMagic_Throws()
    {
        var header = new byte[DvripClient.HeaderSize];

        await Assert.That(() => DvripClient.DecodeHeader(header)).Throws<DvripException>();
    }

    [Test]
    public async Task ParsePayload_TrailingZeroAndGarbage_Ignored()
    {
        var body = Encoding.UTF8.GetBytes("{\"Ret\":100,\"SessionID\":\"0x0000000B\"}\n\0xyz");

        var json = DvripClient.ParsePayload(body);

        await Assert.That(DvripClient.RetOf(json)).IsEqualTo(DvripClient.RetOk);
        await Assert.That(DvripClient.ParseSession(json["SessionID"]!.GetValue<string>())).IsEqualTo(11u);
    }

    [Test]
    [Arguments("0x0000000B", 11u)]
    [Arguments("0000001A", 26u)]
    [Arguments(null, 0u)]
    [Arguments("basura", 0u)]
    public async Task ParseSession_Formats(string? value, uint expected)
    {
        await Assert.That(DvripClient.ParseSession(value)).IsEqualTo(expected);
    }

    [Test]
    public async Task FormatSession_IsHexWithPrefix()
    {
        await Assert.That(DvripClient.FormatSession(11)).IsEqualTo("0x0000000B");
    }

    [Test]
    public async Task RetOf_Missing_Null()
    {
        await Assert.That(DvripClient.RetOf(new JsonObject())).IsNull();
    }
}
