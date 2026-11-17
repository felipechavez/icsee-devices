using System.Xml.Linq;
using CamGuard.Api.Features.Onvif;

namespace CamGuard.Tests.Features.Onvif;

public class OnvifSoapTests
{
    [Test]
    public async Task PasswordDigest_KnownVector_MatchesOnvifGuide()
    {
        // Ejemplo de la guía de programación de ONVIF (WS-UsernameToken).
        var nonce = Convert.FromBase64String("LKqI6G/AikKCQrN0zqZFlg==");

        var digest = OnvifSoap.PasswordDigest(nonce, "2010-09-16T07:50:45Z", "userpassword");

        await Assert.That(digest).IsEqualTo("tuOSpGlFlIXsozq4HFNeeGeFLEI=");
    }

    [Test]
    public async Task FormatCreated_UsesUtcWithMilliseconds()
    {
        var at = new DateTimeOffset(2026, 11, 16, 22, 3, 13, 250, TimeSpan.FromHours(-3));

        await Assert.That(OnvifSoap.FormatCreated(at)).IsEqualTo("2026-11-17T01:03:13.250Z");
    }

    [Test]
    public async Task Envelope_WithCredentials_HasUsernameTokenAndAddressing()
    {
        var nonce = new byte[16];
        var created = new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);

        var xml = OnvifSoap.Envelope(OnvifSoap.Actions.GetCapabilities, "http://192.168.1.102/onvif/device_service",
            OnvifSoap.Bodies.GetCapabilities, new OnvifCredentials("admin", "secreta"), created, nonce);
        var doc = XDocument.Parse(xml);

        await Assert.That(Local(doc, "Username")).IsEqualTo("admin");
        await Assert.That(Local(doc, "Created")).IsEqualTo("2026-11-17T01:00:00.000Z");
        await Assert.That(Local(doc, "Password")).IsEqualTo(OnvifSoap.PasswordDigest(nonce, "2026-11-17T01:00:00.000Z", "secreta"));
        await Assert.That(Local(doc, "Action")).IsEqualTo(OnvifSoap.Actions.GetCapabilities);
        await Assert.That(Local(doc, "To")).IsEqualTo("http://192.168.1.102/onvif/device_service");
        await Assert.That(xml).DoesNotContain("secreta");
    }

    [Test]
    public async Task Envelope_WithoutCredentials_HasNoSecurityHeader()
    {
        var xml = OnvifSoap.Envelope(OnvifSoap.Actions.GetSystemDateAndTime, "http://x/onvif/device_service",
            OnvifSoap.Bodies.GetSystemDateAndTime, null, DateTimeOffset.UnixEpoch, new byte[16]);

        await Assert.That(xml).DoesNotContain("Security");
    }

    [Test]
    public async Task Envelope_UsernameWithXmlCharacters_IsEscaped()
    {
        var xml = OnvifSoap.Envelope("a", "http://x", "<b/>", new OnvifCredentials("ad<m>in&", "x"), DateTimeOffset.UnixEpoch, new byte[16]);

        await Assert.That(Local(XDocument.Parse(xml), "Username")).IsEqualTo("ad<m>in&");
    }

    [Test]
    [Arguments(10, "PT10S")]
    [Arguments(0.2, "PT1S")]
    [Arguments(120, "PT120S")]
    public async Task Duration_WholeSecondsAtLeastOne(double seconds, string expected)
    {
        await Assert.That(OnvifSoap.Duration(TimeSpan.FromSeconds(seconds))).IsEqualTo(expected);
    }

    private static string? Local(XDocument doc, string name) => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
}
