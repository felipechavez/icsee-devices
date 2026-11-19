using System.Text;
using CamGuard.Api.Features.Onvif;

namespace CamGuard.Tests.Features.Onvif;

public class OnvifClientTests
{
    [Test]
    [Arguments("%s")]
    [Arguments("no-existe")]
    [Arguments(null)]
    [Arguments("")]
    public async Task DecodeBody_InvalidOrMissingCharset_Utf8(string? charset)
    {
        var text = OnvifClient.DecodeBody(Encoding.UTF8.GetBytes("<Cámara/>"), charset);

        await Assert.That(text).IsEqualTo("<Cámara/>");
    }

    [Test]
    [Arguments("iso-8859-1")]
    [Arguments("\"ISO-8859-1\"")]
    public async Task DecodeBody_DeclaredCharset_Used(string charset)
    {
        var text = OnvifClient.DecodeBody(Encoding.Latin1.GetBytes("<Cámara/>"), charset);

        await Assert.That(text).IsEqualTo("<Cámara/>");
    }

    [Test]
    public async Task OnvifException_SoapFault_Flagged()
    {
        const string fault = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body><s:Fault>
              <s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text>Sender not Authorized</s:Text></s:Reason>
            </s:Fault></s:Body></s:Envelope>
            """;

        var ex = Assert.Throws<OnvifException>(() => OnvifParser.Parse(fault));

        await Assert.That(ex.SoapFault).IsTrue();
    }
}
