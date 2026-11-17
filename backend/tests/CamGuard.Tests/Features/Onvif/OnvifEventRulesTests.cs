using CamGuard.Api.Features.Onvif;

namespace CamGuard.Tests.Features.Onvif;

public class OnvifEventRulesTests
{
    private static OnvifNotification Message(string topic, string item, string value, string operation = "Changed") =>
        new(topic, operation, null, new Dictionary<string, string>(), new Dictionary<string, string> { [item] = value });

    [Test]
    [Arguments("VideoSource/MotionAlarm", "State", "true", true)]
    [Arguments("VideoSource/MotionAlarm", "State", "false", false)]
    [Arguments("RuleEngine/CellMotionDetector/Motion", "IsMotion", "true", true)]
    [Arguments("RuleEngine/MotionDetector/Motion", "State", "1", true)]
    public async Task Interpret_MotionTopics_GiveMotion(string topic, string item, string value, bool active)
    {
        var signal = OnvifEventRules.Interpret(Message(topic, item, value));

        await Assert.That(signal).IsEqualTo(new OnvifSignal(OnvifSignalKind.Motion, active, Initial: false));
    }

    [Test]
    public async Task Interpret_InitializedState_MarkedInitial()
    {
        var signal = OnvifEventRules.Interpret(Message("VideoSource/MotionAlarm", "State", "false", "Initialized"));

        await Assert.That(signal!.Initial).IsTrue();
    }

    [Test]
    [Arguments("RuleEngine/TamperDetector/Tamper", "IsTamper", "true")]
    [Arguments("RuleEngine/TamperDetector/Tamper", "State", "true")]
    public async Task Interpret_Tamper_GivesTamper(string topic, string item, string value)
    {
        var signal = OnvifEventRules.Interpret(Message(topic, item, value));

        await Assert.That(signal).IsEqualTo(new OnvifSignal(OnvifSignalKind.Tamper, true, Initial: false));
    }

    [Test]
    [Arguments("VideoSource/GlobalSceneChange/ImagingService", "State", "true")]
    [Arguments("Media/ProfileChanged", "Token", "x")]
    [Arguments("Device/Trigger/DigitalInput", "LogicalState", "true")]
    [Arguments("VideoSource/MotionAlarm", "Otro", "true")]
    [Arguments("VideoSource/MotionAlarm", "State", "quizas")]
    public async Task Interpret_UnknownOrIgnored_IsNull(string topic, string item, string value)
    {
        await Assert.That(OnvifEventRules.Interpret(Message(topic, item, value))).IsNull();
    }
}
