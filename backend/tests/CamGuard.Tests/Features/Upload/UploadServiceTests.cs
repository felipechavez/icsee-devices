using CamGuard.Api.Features.Upload;

namespace CamGuard.Tests.Features.Upload;

public class UploadServiceTests
{
    [Test]
    [Arguments("persona", "Persona: Entrada")]
    [Arguments("perro", "Animal (perro): Entrada")]
    [Arguments("nada", "Movimiento: Entrada")]
    [Arguments(null, "Movimiento: Entrada")]
    public async Task NotificationTitle_UsesDetection(string? detection, string expected)
    {
        await Assert.That(UploadService.NotificationTitle(detection, "Entrada")).IsEqualTo(expected);
    }
}
