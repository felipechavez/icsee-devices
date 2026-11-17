using CamGuard.Api.Features.Onvif;

namespace CamGuard.Tests.Features.Onvif;

/// <summary>Respuestas reales (recortadas) de una Imou Cue 2, capturadas con poc/imou/onvif-events.ps1.</summary>
public class OnvifParserTests
{
    private const string Envelope =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" " +
        "xmlns:tt=\"http://www.onvif.org/ver10/schema\" xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\" " +
        "xmlns:trt=\"http://www.onvif.org/ver10/media/wsdl\" xmlns:tev=\"http://www.onvif.org/ver10/events/wsdl\" " +
        "xmlns:wsa5=\"http://www.w3.org/2005/08/addressing\" xmlns:wsnt=\"http://docs.oasis-open.org/wsn/b-2\" " +
        "xmlns:tns1=\"http://www.onvif.org/ver10/topics\"><s:Body>{0}</s:Body></s:Envelope>";

    private static string Wrap(string body) => string.Format(Envelope, body);

    private static readonly string Capabilities = Wrap(
        "<tds:GetCapabilitiesResponse><tds:Capabilities>" +
        "<tt:Analytics><tt:XAddr>http://192.168.1.102/onvif/analytics_service</tt:XAddr></tt:Analytics>" +
        "<tt:Device><tt:XAddr>http://192.168.1.102/onvif/device_service</tt:XAddr></tt:Device>" +
        "<tt:Events><tt:XAddr>http://192.168.1.102/onvif/event_service</tt:XAddr><tt:WSSubscriptionPolicySupport>true</tt:WSSubscriptionPolicySupport>" +
        "<tt:WSPullPointSupport>true</tt:WSPullPointSupport></tt:Events>" +
        "<tt:Media><tt:XAddr>http://192.168.1.102/onvif/media_service</tt:XAddr></tt:Media>" +
        "<tt:Extension><tt:DeviceIO><tt:XAddr>http://192.168.1.102/onvif/deviceIO_service</tt:XAddr></tt:DeviceIO></tt:Extension>" +
        "</tds:Capabilities></tds:GetCapabilitiesResponse>");

    [Test]
    public async Task Capabilities_ImouResponse_ReadsMediaAndEvents()
    {
        var caps = OnvifParser.Capabilities(OnvifParser.Parse(Capabilities));

        await Assert.That(caps.MediaXAddr).IsEqualTo("http://192.168.1.102/onvif/media_service");
        await Assert.That(caps.EventsXAddr).IsEqualTo("http://192.168.1.102/onvif/event_service");
        await Assert.That(caps.PullPointSupport).IsTrue();
    }

    [Test]
    public async Task ClockOffset_CameraAhead_IsPositive()
    {
        var doc = OnvifParser.Parse(Wrap(
            "<tds:GetSystemDateAndTimeResponse><tds:SystemDateAndTime><tt:UTCDateTime>" +
            "<tt:Time><tt:Hour>1</tt:Hour><tt:Minute>0</tt:Minute><tt:Second>10</tt:Second></tt:Time>" +
            "<tt:Date><tt:Year>2026</tt:Year><tt:Month>11</tt:Month><tt:Day>17</tt:Day></tt:Date>" +
            "</tt:UTCDateTime></tds:SystemDateAndTime></tds:GetSystemDateAndTimeResponse>"));

        var offset = OnvifParser.ClockOffset(doc, new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero));

        await Assert.That(offset).IsEqualTo(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task ClockOffset_WithoutUtc_IsNull()
    {
        var doc = OnvifParser.Parse(Wrap("<tds:GetSystemDateAndTimeResponse/>"));

        await Assert.That(OnvifParser.ClockOffset(doc, DateTimeOffset.UnixEpoch)).IsNull();
    }

    [Test]
    public async Task Profiles_ChoosesHighestResolutionAsMain()
    {
        var doc = OnvifParser.Parse(Wrap(
            "<trt:GetProfilesResponse>" +
            Profile("MediaProfile00001", "SubStream", "H264", 640, 480) +
            Profile("MediaProfile00000", "MainStream", "H264", 1920, 1080) +
            "</trt:GetProfilesResponse>"));

        var (main, sub) = OnvifParser.ChooseProfiles(OnvifParser.Profiles(doc))!.Value;

        await Assert.That(main.Token).IsEqualTo("MediaProfile00000");
        await Assert.That(main.Width).IsEqualTo(1920);
        await Assert.That(sub.Token).IsEqualTo("MediaProfile00001");
    }

    [Test]
    public async Task ChooseProfiles_SingleProfile_UsedForBoth()
    {
        var only = new OnvifProfile("p0", null, "H264", 1280, 720);

        var (main, sub) = OnvifParser.ChooseProfiles([only])!.Value;

        await Assert.That(main).IsEqualTo(only);
        await Assert.That(sub).IsEqualTo(only);
    }

    [Test]
    public async Task ChooseProfiles_None_IsNull()
    {
        await Assert.That(OnvifParser.ChooseProfiles([])).IsNull();
    }

    [Test]
    public async Task SubscriptionAddress_ImouResponse_Read()
    {
        var doc = OnvifParser.Parse(Wrap(
            "<tev:CreatePullPointSubscriptionResponse><tev:SubscriptionReference>" +
            "<wsa5:Address>http://192.168.1.102/onvif/Subscription?Idx=2</wsa5:Address></tev:SubscriptionReference>" +
            "<wsnt:CurrentTime>2026-11-17T01:03:13Z</wsnt:CurrentTime></tev:CreatePullPointSubscriptionResponse>"));

        await Assert.That(OnvifParser.SubscriptionAddress(doc)).IsEqualTo("http://192.168.1.102/onvif/Subscription?Idx=2");
    }

    [Test]
    [Arguments("http://192.168.1.102/onvif/Subscription?Idx=2", "http://192.168.1.102:80/onvif/Subscription?Idx=2")]
    [Arguments("http://0.0.0.0/onvif/Subscription?Idx=2", "http://192.168.1.102:80/onvif/Subscription?Idx=2")]
    [Arguments("http://192.168.1.108:8080/onvif/Subscription?Idx=2", "http://192.168.1.102:80/onvif/Subscription?Idx=2")]
    [Arguments("/onvif/Subscription?Idx=2", "http://192.168.1.102:80/onvif/Subscription?Idx=2")]
    [Arguments("", "http://192.168.1.102:80/onvif/event_service")]
    [Arguments(null, "http://192.168.1.102:80/onvif/event_service")]
    public async Task RebaseHttp_AlwaysPointsToConfiguredCamera(string? address, string expected)
    {
        var uri = OnvifParser.RebaseHttp(address, "192.168.1.102", 80, "/onvif/event_service");

        await Assert.That(uri.OriginalString).IsEqualTo(expected);
    }

    [Test]
    [Arguments("rtsp://192.168.1.108:554/cam/realmonitor?channel=1&subtype=0", "rtsp://192.168.1.102:554/cam/realmonitor?channel=1&subtype=0")]
    [Arguments("rtsp://192.168.1.102/cam/realmonitor?channel=1&subtype=1", "rtsp://192.168.1.102:554/cam/realmonitor?channel=1&subtype=1")]
    [Arguments("rtsp://admin:x@192.168.1.102:8554/live", "rtsp://192.168.1.102:8554/live")]
    public async Task RebaseRtsp_ForcesHostAndDropsCredentials(string uri, string expected)
    {
        await Assert.That(OnvifParser.RebaseRtsp(uri, "192.168.1.102")).IsEqualTo(expected);
    }

    [Test]
    [Arguments("http://192.168.1.102/x")]
    [Arguments("no es una uri")]
    [Arguments("")]
    public async Task RebaseRtsp_NotRtsp_IsNull(string uri)
    {
        await Assert.That(OnvifParser.RebaseRtsp(uri, "192.168.1.102")).IsNull();
    }

    [Test]
    public async Task Notifications_MotionAlarm_ReadsTopicOperationAndState()
    {
        var doc = OnvifParser.Parse(Wrap(
            "<tev:PullMessagesResponse>" +
            Notification("tns1:VideoSource/MotionAlarm", "Initialized", "2026-11-17T01:03:13Z", "false") +
            Notification("tns1:VideoSource/MotionAlarm", "Changed", "2026-11-17T01:03:36Z", "true") +
            "</tev:PullMessagesResponse>"));

        var messages = OnvifParser.Notifications(doc);

        await Assert.That(messages.Count).IsEqualTo(2);
        await Assert.That(messages[1].Topic).IsEqualTo("VideoSource/MotionAlarm");
        await Assert.That(messages[1].Operation).IsEqualTo("Changed");
        await Assert.That(messages[1].Data["State"]).IsEqualTo("true");
        await Assert.That(messages[1].Source["Source"]).IsEqualTo("VideoSource000");
        await Assert.That(messages[1].UtcTime).IsEqualTo(new DateTimeOffset(2026, 11, 17, 1, 3, 36, TimeSpan.Zero));
    }

    [Test]
    public async Task Notifications_EmptyPull_NoMessages()
    {
        var doc = OnvifParser.Parse(Wrap("<tev:PullMessagesResponse><tev:CurrentTime>2026-11-17T01:03:13Z</tev:CurrentTime></tev:PullMessagesResponse>"));

        await Assert.That(OnvifParser.Notifications(doc)).IsEmpty();
    }

    [Test]
    [Arguments("tns1:VideoSource/MotionAlarm", "VideoSource/MotionAlarm")]
    [Arguments("tns1:RuleEngine/tnsdahua:CellMotionDetector/Motion", "RuleEngine/CellMotionDetector/Motion")]
    [Arguments(" tns1:RuleEngine/TamperDetector/Tamper ", "RuleEngine/TamperDetector/Tamper")]
    public async Task NormalizeTopic_StripsNamespacePrefixes(string topic, string expected)
    {
        await Assert.That(OnvifParser.NormalizeTopic(topic)).IsEqualTo(expected);
    }

    [Test]
    public async Task Parse_NotAuthorizedFault_ThrowsUnauthorized()
    {
        var xml = Wrap(
            "<s:Fault><s:Code><s:Value>s:Sender</s:Value><s:Subcode><s:Value>ter:NotAuthorized</s:Value></s:Subcode></s:Code>" +
            "<s:Reason><s:Text xml:lang=\"en\">Sender not Authorized</s:Text></s:Reason></s:Fault>");

        var ex = await Assert.That(() => OnvifParser.Parse(xml)).Throws<OnvifException>();

        await Assert.That(ex.Unauthorized).IsTrue();
        await Assert.That(ex.Message).Contains("Sender not Authorized");
    }

    [Test]
    public async Task Parse_OtherFault_NotUnauthorized()
    {
        var xml = Wrap("<s:Fault><s:Code><s:Value>s:Receiver</s:Value></s:Code><s:Reason><s:Text>Action Not Supported</s:Text></s:Reason></s:Fault>");

        var ex = await Assert.That(() => OnvifParser.Parse(xml)).Throws<OnvifException>();

        await Assert.That(ex.Unauthorized).IsFalse();
    }

    [Test]
    public async Task Parse_Dtd_Rejected()
    {
        const string xml = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><x>&e;</x>";

        await Assert.That(() => OnvifParser.Parse(xml)).Throws<OnvifException>();
    }

    [Test]
    public async Task DeviceInfo_DisplayModel_JoinsManufacturerAndModel()
    {
        var doc = OnvifParser.Parse(Wrap(
            "<tds:GetDeviceInformationResponse><tds:Manufacturer>Imou</tds:Manufacturer><tds:Model>IPC-C22EP</tds:Model>" +
            "<tds:FirmwareVersion>2.840</tds:FirmwareVersion></tds:GetDeviceInformationResponse>"));

        await Assert.That(OnvifParser.DeviceInfo(doc).DisplayModel).IsEqualTo("Imou IPC-C22EP");
    }

    private static string Profile(string token, string name, string encoding, int width, int height) =>
        $"<trt:Profiles token=\"{token}\" fixed=\"true\"><tt:Name>{name}</tt:Name>" +
        $"<tt:VideoEncoderConfiguration token=\"VE{token}\"><tt:Name>{name}</tt:Name><tt:Encoding>{encoding}</tt:Encoding>" +
        $"<tt:Resolution><tt:Width>{width}</tt:Width><tt:Height>{height}</tt:Height></tt:Resolution></tt:VideoEncoderConfiguration></trt:Profiles>";

    private static string Notification(string topic, string operation, string at, string state) =>
        "<wsnt:NotificationMessage>" +
        $"<wsnt:Topic Dialect=\"http://www.onvif.org/ver10/tev/topicExpression/ConcreteSet\">{topic}</wsnt:Topic>" +
        $"<wsnt:Message><tt:Message UtcTime=\"{at}\" PropertyOperation=\"{operation}\">" +
        "<tt:Source><tt:SimpleItem Name=\"Source\" Value=\"VideoSource000\"/></tt:Source>" +
        $"<tt:Data><tt:SimpleItem Name=\"State\" Value=\"{state}\"/></tt:Data>" +
        "</tt:Message></wsnt:Message></wsnt:NotificationMessage>";
}
