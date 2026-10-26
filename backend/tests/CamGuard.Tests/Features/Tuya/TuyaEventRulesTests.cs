using System.Text.Json.Nodes;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Tuya;

namespace CamGuard.Tests.Features.Tuya;

public class TuyaEventRulesTests
{
    [Test]
    public async Task Interpret_DoorbellTrue_TriggersRecording()
    {
        var signal = TuyaEventRules.Interpret("doorbell", JsonValue.Create(true));

        await Assert.That(signal?.Kind).IsEqualTo(TuyaSignalKind.Doorbell);
        await Assert.That(signal!.TriggersRecording).IsTrue();
        await Assert.That(signal.EventType).IsEqualTo(CameraEventType.Doorbell);
        await Assert.That(signal.Text).IsEqualTo("Tocaron el timbre");
    }

    [Test]
    [Arguments("https://ty-us-storage.tuyaus.com/x.jpg")]
    [Arguments("eyJ2Ijoi")]
    public async Task Interpret_CameraMotionPicture_TriggersRecording(string picture)
    {
        var signal = TuyaEventRules.Interpret("movement_detect_pic", JsonValue.Create(picture))!;

        await Assert.That(signal.Kind).IsEqualTo(TuyaSignalKind.Motion);
        await Assert.That(signal.TriggersRecording).IsTrue();
        await Assert.That(signal.EventType).IsEqualTo(CameraEventType.Motion);
        await Assert.That(signal.Text).IsEqualTo("Movimiento detectado");
    }

    [Test]
    public async Task Interpret_EmptyMotionPicture_Ignored()
    {
        await Assert.That(TuyaEventRules.Interpret("movement_detect_pic", JsonValue.Create(""))).IsNull();
    }

    [Test]
    [Arguments("doorbell_active", "1760321000")]
    [Arguments("doorbell_pic", "https://ty-us-storage.tuyaus.com/x.jpg")]
    public async Task Interpret_CameraDoorbell_TriggersRecording(string code, string value)
    {
        var signal = TuyaEventRules.Interpret(code, JsonValue.Create(value))!;

        await Assert.That(signal.Kind).IsEqualTo(TuyaSignalKind.Doorbell);
        await Assert.That(signal.TriggersRecording).IsTrue();
    }

    [Test]
    [Arguments("wireless_electricity")]
    [Arguments("battery_percentage")]
    public async Task Interpret_CameraBattery_ReturnsPercent(string code)
    {
        var signal = TuyaEventRules.Interpret(code, JsonValue.Create(73))!;

        await Assert.That(signal.Kind).IsEqualTo(TuyaSignalKind.Battery);
        await Assert.That(signal.BatteryPercent).IsEqualTo(73);
    }
    // Mensaje real de la XOOMI C20 (rutas de archivos acortadas).
    private const string LockMedia =
        """{"v":"5.0","cmd":"door_lock_video","type":"media","with":"resources","alarm":true,"time":1791837012,"files":[["ty-us-bizlock","/x/1.jpg","k","1"]],"ext":{"record":true,"id":0,"type":8,"fileId":"0"}}""";

    private static JsonNode Base64(string json) => JsonValue.Create(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)));

    [Test]
    public async Task Interpret_UnknownLockMediaType_RecordsAsLockEvent()
    {
        var signal = TuyaEventRules.Interpret("initiative_message", Base64(LockMedia.Replace("\"type\":8", "\"type\":16")))!;

        await Assert.That(signal.Kind).IsEqualTo(TuyaSignalKind.LockEvent);
        await Assert.That(signal.Detail).IsEqualTo("media #16");
        await Assert.That(signal.TriggersRecording).IsTrue();
        await Assert.That(signal.EventType).IsEqualTo(CameraEventType.LockEvent);
        await Assert.That(signal.Text).Contains("tipo 16");
    }

    [Test]
    public async Task Interpret_LockMediaType8_FingerprintPhoto_Ignored()
    {
        // La apertura ya llega por unlock_fingerprint y no graba.
        await Assert.That(TuyaEventRules.Interpret("initiative_message", Base64(LockMedia))).IsNull();
    }

    [Test]
    public async Task Interpret_LockMediaType18_IsDoorbellLikeTheDp()
    {
        var media = TuyaEventRules.Interpret("initiative_message", Base64(LockMedia.Replace("\"type\":8", "\"type\":18")))!;
        var dp = TuyaEventRules.Interpret("doorbell", JsonValue.Create(true))!;

        await Assert.That(media.Kind).IsEqualTo(TuyaSignalKind.Doorbell);
        // Mismo Kind y Detail: el servicio avisa una sola vez por timbre.
        await Assert.That(media.Detail).IsEqualTo(dp.Detail);
    }

    [Test]
    public async Task Interpret_InitiativeMessageOtherCommand_Ignored()
    {
        await Assert.That(TuyaEventRules.Interpret("initiative_message", Base64("""{"v":"5.0","cmd":"remote_unlock_request"}"""))).IsNull();
    }

    [Test]
    [Arguments("")]
    [Arguments("no-es-base64!!")]
    public async Task Interpret_InitiativeMessageInvalid_Ignored(string value)
    {
        await Assert.That(TuyaEventRules.Interpret("initiative_message", JsonValue.Create(value))).IsNull();
    }

    [Test]
    [Arguments("pir")]
    [Arguments(true)]
    public async Task Interpret_PirSensorDetecting_IsMotion(object value)
    {
        var node = value is bool b ? JsonValue.Create(b) : JsonValue.Create((string)value);

        await Assert.That(TuyaEventRules.Interpret("pir", node)?.Kind).IsEqualTo(TuyaSignalKind.Motion);
    }

    [Test]
    public async Task Interpret_UppercasePirCode_IsMotion()
    {
        await Assert.That(TuyaEventRules.Interpret("PIR", JsonValue.Create(true))?.Kind).IsEqualTo(TuyaSignalKind.Motion);
    }

    [Test]
    [Arguments("none")]
    [Arguments(false)]
    public async Task Interpret_PirSensorIdle_Ignored(object value)
    {
        var node = value is bool b ? JsonValue.Create(b) : JsonValue.Create((string)value);

        await Assert.That(TuyaEventRules.Interpret("pir", node)).IsNull();
    }

    [Test]
    public async Task Interpret_DoorbellFalse_Ignored()
    {
        await Assert.That(TuyaEventRules.Interpret("doorbell", JsonValue.Create(false))).IsNull();
    }

    [Test]
    [Arguments("wrong_password", "Alarma: clave errónea")]
    [Arguments("wrong_finger", "Alarma: huella no reconocida")]
    [Arguments("tongue_bad", "Alarma: cerrojo bloqueado o forzado")]
    public async Task Interpret_Alarm_TriggersRecordingWithText(string alarm, string text)
    {
        var signal = TuyaEventRules.Interpret("alarm_lock", JsonValue.Create(alarm))!;

        await Assert.That(signal.Kind).IsEqualTo(TuyaSignalKind.Alarm);
        await Assert.That(signal.Detail).IsEqualTo(alarm);
        await Assert.That(signal.Text).IsEqualTo(text);
        await Assert.That(signal.TriggersRecording).IsTrue();
    }

    [Test]
    [Arguments("low_battery")]
    [Arguments("too_hot")]
    public async Task Interpret_NonIntrusionAlarm_DoesNotRecord(string alarm)
    {
        var signal = TuyaEventRules.Interpret("alarm_lock", JsonValue.Create(alarm))!;

        await Assert.That(signal.TriggersRecording).IsFalse();
        await Assert.That(signal.EventType).IsEqualTo(CameraEventType.Alarm);
    }

    [Test]
    public async Task Interpret_UnknownAlarm_UsesRawCode()
    {
        var signal = TuyaEventRules.Interpret("alarm_lock", JsonValue.Create("door_open"))!;

        await Assert.That(signal.Text).IsEqualTo("Alarma: door_open");
    }

    [Test]
    public async Task Interpret_Hijack_TriggersRecording()
    {
        var signal = TuyaEventRules.Interpret("hijack", JsonValue.Create(true))!;

        await Assert.That(signal.Kind).IsEqualTo(TuyaSignalKind.Hijack);
        await Assert.That(signal.TriggersRecording).IsTrue();
        await Assert.That(signal.EventType).IsEqualTo(CameraEventType.Alarm);
    }

    [Test]
    [Arguments("unlock_fingerprint", 1, "fingerprint #1", "Abrieron con huella (usuario 1)")]
    [Arguments("unlock_password", 6, "password #6", "Abrieron con clave (usuario 6)")]
    [Arguments("unlock_phone_remote", 256, "phone_remote #256", "Abrieron con la app (usuario 256)")]
    public async Task Interpret_Unlock_RecordsWithoutRecording(string code, int user, string detail, string text)
    {
        var signal = TuyaEventRules.Interpret(code, JsonValue.Create(user))!;

        await Assert.That(signal.Kind).IsEqualTo(TuyaSignalKind.Unlock);
        await Assert.That(signal.Detail).IsEqualTo(detail);
        await Assert.That(signal.Text).IsEqualTo(text);
        await Assert.That(signal.TriggersRecording).IsFalse();
        await Assert.That(signal.EventType).IsEqualTo(CameraEventType.Unlock);
    }

    [Test]
    public async Task Interpret_Battery_ReturnsPercent()
    {
        var signal = TuyaEventRules.Interpret("residual_electricity", JsonValue.Create(48))!;

        await Assert.That(signal.Kind).IsEqualTo(TuyaSignalKind.Battery);
        await Assert.That(signal.BatteryPercent).IsEqualTo(48);
        await Assert.That(signal.EventType).IsNull();
        await Assert.That(signal.TriggersRecording).IsFalse();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(101)]
    public async Task Interpret_BatteryOutOfRange_Ignored(int value)
    {
        await Assert.That(TuyaEventRules.Interpret("residual_electricity", JsonValue.Create(value))).IsNull();
    }

    [Test]
    [Arguments("link_mode")]
    [Arguments("lock_record")]
    [Arguments("unlock_double_kit")]
    public async Task Interpret_OtherCodes_Ignored(string code)
    {
        await Assert.That(TuyaEventRules.Interpret(code, JsonValue.Create("x"))).IsNull();
    }
}
