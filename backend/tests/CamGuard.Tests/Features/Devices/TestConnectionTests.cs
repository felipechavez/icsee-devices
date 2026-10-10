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
}
