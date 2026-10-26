using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Cameras;

namespace CamGuard.Tests.Features.Cameras;

public class CameraInputTests
{
    private static CameraInput Valid() => new("Entrada", "192.168.1.86", 34567, "icsee", true, "admin", "secreta");

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Validate_ValidRequest_NoErrors(bool isNew)
    {
        await Assert.That(CameraInput.Validate(Valid(), isNew)).IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Validate_EmptyName_Rejected(string name)
    {
        var errors = CameraInput.Validate(Valid() with { Name = name }, isNew: false);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.Name));
    }

    [Test]
    [Arguments("999.1.1.1")]
    [Arguments("camara")]
    [Arguments("")]
    public async Task Validate_InvalidIp_Rejected(string ip)
    {
        var errors = CameraInput.Validate(Valid() with { Ip = ip }, isNew: false);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.Ip));
    }

    [Test]
    [Arguments(0)]
    [Arguments(65536)]
    public async Task Validate_PortOutOfRange_Rejected(int port)
    {
        var errors = CameraInput.Validate(Valid() with { DvripPort = port }, isNew: false);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.DvripPort));
    }

    [Test]
    [Arguments("icsee/../admin")]
    [Arguments("cam 1")]
    [Arguments("cam?src=x")]
    public async Task Validate_UnsafeStreamName_Rejected(string stream)
    {
        var errors = CameraInput.Validate(Valid() with { Go2rtcStream = stream }, isNew: false);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.Go2rtcStream));
    }

    [Test]
    [Arguments("icsee")]
    [Arguments("patio_trasero-2")]
    public async Task Validate_SafeStreamName_Accepted(string stream)
    {
        await Assert.That(CameraInput.Validate(Valid() with { Go2rtcStream = stream }, isNew: false)).IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    public async Task Validate_EmptyUsername_Rejected(string username)
    {
        var errors = CameraInput.Validate(Valid() with { Username = username }, isNew: false);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.Username));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Validate_NewWithoutPassword_Rejected(string? password)
    {
        var errors = CameraInput.Validate(Valid() with { Password = password }, isNew: true);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.Password));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Validate_EditWithoutPassword_KeepsCurrent(string? password)
    {
        await Assert.That(CameraInput.Validate(Valid() with { Password = password }, isNew: false)).IsEmpty();
    }

    [Test]
    public async Task Validate_PasswordTooLong_Rejected()
    {
        var errors = CameraInput.Validate(Valid() with { Password = new string('x', CameraInput.MaxPasswordLength + 1) }, isNew: false);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.Password));
    }

    [Test]
    public async Task Validate_ModelTooLong_Rejected()
    {
        var errors = CameraInput.Validate(Valid() with { Model = new string('m', 101) }, isNew: false);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.Model));
    }

    [Test]
    public async Task Validate_UnknownSensitivity_Rejected()
    {
        var errors = CameraInput.Validate(Valid() with { MotionSensitivity = (MotionSensitivity)9 }, isNew: false);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.MotionSensitivity));
    }

    private static CameraInput ValidTuya() =>
        new("Chapa de casa", "", 0, "chapa", true, "", Kind: CameraKind.Tuya, TuyaDeviceId: "eb8d8e837c0a688a86f1kb", Rotation: 90);

    [Test]
    public async Task Validate_TuyaWithoutIpOrCredentials_Accepted()
    {
        await Assert.That(CameraInput.Validate(ValidTuya(), isNew: true)).IsEmpty();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("corto")]
    [Arguments("eb8d8e837c/../x")]
    public async Task Validate_TuyaInvalidDeviceId_Rejected(string? deviceId)
    {
        var errors = CameraInput.Validate(ValidTuya() with { TuyaDeviceId = deviceId }, isNew: true);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.TuyaDeviceId));
    }

    [Test]
    [Arguments(45)]
    [Arguments(360)]
    [Arguments(-90)]
    public async Task Validate_InvalidRotation_Rejected(int rotation)
    {
        var errors = CameraInput.Validate(Valid() with { Rotation = rotation }, isNew: false);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.Rotation));
    }

    [Test]
    public async Task Validate_UnknownKind_Rejected()
    {
        var errors = CameraInput.Validate(Valid() with { Kind = (CameraKind)9 }, isNew: true);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.Kind));
    }

    [Test]
    public async Task ValidateKindUnchanged_DifferentKind_Rejected()
    {
        var camera = new Camera { Name = "Entrada", Ip = "192.168.1.86", Go2rtcStream = "icsee", Kind = CameraKind.Icsee };

        await Assert.That(CameraInput.ValidateKindUnchanged(ValidTuya(), camera).Keys).Contains(nameof(CameraInput.Kind));
        await Assert.That(CameraInput.ValidateKindUnchanged(Valid(), camera)).IsEmpty();
    }

    [Test]
    public async Task ApplyTo_TuyaTriggers_NormalizedCommaSeparated()
    {
        var camera = new Camera { Name = "", Ip = "", Go2rtcStream = "" };

        (ValidTuya() with { TuyaTriggerDeviceIds = " eb38d1b64239595ec2jrvu ; eb38d1b64239595ec2jrvu\nebd88662226f705967vylf " }).ApplyTo(camera, null!);

        await Assert.That(camera.TuyaTriggerDeviceIds).IsEqualTo("eb38d1b64239595ec2jrvu,ebd88662226f705967vylf");
    }

    [Test]
    [Arguments("corto")]
    [Arguments("eb8d8e837c0a688a86f1kb")]
    [Arguments("eb38d1b64239595ec2jrvu/../x")]
    public async Task Validate_InvalidTriggerDevice_Rejected(string triggers)
    {
        var errors = CameraInput.Validate(ValidTuya() with { TuyaTriggerDeviceIds = triggers }, isNew: true);

        await Assert.That(errors.Keys).Contains(nameof(CameraInput.TuyaTriggerDeviceIds));
    }

    [Test]
    public async Task ApplyTo_Tuya_ClearsIpAndKeepsDeviceId()
    {
        var camera = new Camera { Name = "", Ip = "192.168.1.5", Go2rtcStream = "" };

        ValidTuya().ApplyTo(camera, null!);

        await Assert.That(camera.Kind).IsEqualTo(CameraKind.Tuya);
        await Assert.That(camera.Ip).IsEqualTo("");
        await Assert.That(camera.TuyaDeviceId).IsEqualTo("eb8d8e837c0a688a86f1kb");
        await Assert.That(camera.Rotation).IsEqualTo(90);
    }
}
