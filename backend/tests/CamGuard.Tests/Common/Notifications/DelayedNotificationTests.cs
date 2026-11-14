using CamGuard.Api.Common.Notifications;

namespace CamGuard.Tests.Common.Notifications;

public class DelayedNotificationTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 1, 15, 4, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromHours(6);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");
    private static readonly Notification Sample = new("Persona: Entrada", "Nuevo clip a las 12:04:00", "https://x/clip", "https://x/img.jpg", ["video_camera"], 4);

    [Test]
    public async Task Prepare_Expired_ReturnsNull()
    {
        await Assert.That(DelayedNotification.Prepare(Sample, Created, Created.AddHours(6).AddMinutes(1), MaxDelay, Zone)).IsNull();
    }

    [Test]
    public async Task Prepare_UnderTwoMinutes_Unchanged()
    {
        await Assert.That(DelayedNotification.Prepare(Sample, Created, Created.AddSeconds(90), MaxDelay, Zone)).IsEqualTo(Sample);
    }

    [Test]
    public async Task Prepare_Delayed_PrefixesLocalTimeAndKeepsTheRest()
    {
        var result = DelayedNotification.Prepare(Sample, Created, Created.AddHours(1), MaxDelay, Zone);

        await Assert.That(result!.Message).IsEqualTo("Retrasado (12:04) · Nuevo clip a las 12:04:00");
        await Assert.That(result with { Message = Sample.Message }).IsEqualTo(Sample);
    }
}
