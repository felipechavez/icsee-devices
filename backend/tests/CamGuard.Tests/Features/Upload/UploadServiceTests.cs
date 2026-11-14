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

    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(30, true)]
    public async Task ShouldProcess_WaitsForPause(int secondsAfterPause, bool expected)
    {
        var pausedUntil = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        await Assert.That(UploadService.ShouldProcess(pausedUntil.AddSeconds(secondsAfterPause), pausedUntil)).IsEqualTo(expected);
    }
}
