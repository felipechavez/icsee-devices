using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Devices;

namespace CamGuard.Tests.Features.Devices;

public class TestConnectionTests
{
    [Test]
    public async Task Validate_ValidRequest_NoErrors()
    {
        await Assert.That(TestConnection.Validate(new(null, "192.168.1.86", 34567, "admin", "x"))).IsEmpty();
    }

    [Test]
    [Arguments("camara", 34567, "admin", "Ip")]
    [Arguments("192.168.1.86", 0, "admin", "DvripPort")]
    [Arguments("192.168.1.86", 34567, " ", "Username")]
    public async Task Validate_InvalidField_Rejected(string ip, int port, string user, string field)
    {
        var errors = TestConnection.Validate(new(null, ip, port, user, null));

        await Assert.That(errors.Keys).Contains(field);
    }

    [Test]
    public async Task Validate_Onvif_UsesOnvifPortNotDvrip()
    {
        var request = new TestConnection.Request(null, "192.168.1.102", 0, "admin", "x", CameraKind.Onvif, OnvifPort: 80);

        await Assert.That(TestConnection.Validate(request)).IsEmpty();
        await Assert.That(TestConnection.Validate(request with { OnvifPort = 0 }).Keys).Contains("OnvifPort");
    }
}
