using CamGuard.Api.Common.Notifications;

namespace CamGuard.Tests.Common.Notifications;

public class NotificationOutboxTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static string TempDirectory() => Path.Combine(Path.GetTempPath(), "camguard-outbox-" + Guid.NewGuid().ToString("N"));

    private static OutboxItem Item(int minutes, string title) =>
        new(Guid.NewGuid(), T0.AddMinutes(minutes), new Notification(title, "msg", Tags: ["bell"], Priority: 4));

    [Test]
    public async Task GetAll_ReturnsCreationOrder()
    {
        var dir = TempDirectory();
        try
        {
            var outbox = new NotificationOutbox(dir);
            await outbox.AddAsync(Item(5, "b"), CancellationToken.None);
            await outbox.AddAsync(Item(1, "a"), CancellationToken.None);
            await outbox.AddAsync(Item(9, "c"), CancellationToken.None);

            var titles = (await outbox.GetAllAsync(CancellationToken.None)).Select(i => i.Notification.Title).ToList();

            await Assert.That(string.Join(",", titles)).IsEqualTo("a,b,c");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Add_OverCap_DropsOldest()
    {
        var dir = TempDirectory();
        try
        {
            var outbox = new NotificationOutbox(dir, maxItems: 2);
            await outbox.AddAsync(Item(1, "a"), CancellationToken.None);
            await outbox.AddAsync(Item(2, "b"), CancellationToken.None);
            var dropped = await outbox.AddAsync(Item(3, "c"), CancellationToken.None);

            var titles = (await outbox.GetAllAsync(CancellationToken.None)).Select(i => i.Notification.Title).ToList();

            await Assert.That(dropped).IsEqualTo(1);
            await Assert.That(string.Join(",", titles)).IsEqualTo("b,c");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Save_IsAtomicAndRoundTrips()
    {
        var dir = TempDirectory();
        try
        {
            var outbox = new NotificationOutbox(dir);
            var item = Item(1, "a");
            await outbox.AddAsync(item, CancellationToken.None);
            await outbox.SaveAsync(item with { Attempts = 3 }, CancellationToken.None);

            var all = await outbox.GetAllAsync(CancellationToken.None);

            await Assert.That(Directory.GetFiles(dir, "*.tmp")).IsEmpty();
            await Assert.That(all.Count).IsEqualTo(1);
            await Assert.That(all[0].Attempts).IsEqualTo(3);
            await Assert.That(all[0].Notification.Tags!).IsEquivalentTo(["bell"]);
            await Assert.That(all[0].Notification.Priority).IsEqualTo(4);

            outbox.Remove(all[0]);
            await Assert.That(outbox.IsEmpty).IsTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task GetAll_CorruptFile_IsDeleted()
    {
        var dir = TempDirectory();
        try
        {
            var outbox = new NotificationOutbox(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "0000000000000000000-x.json"), "{ roto");

            await Assert.That(await outbox.GetAllAsync(CancellationToken.None)).IsEmpty();
            await Assert.That(outbox.IsEmpty).IsTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
