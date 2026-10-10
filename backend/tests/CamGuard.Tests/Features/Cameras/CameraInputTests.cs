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
}
