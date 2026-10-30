using CamGuard.Api.Common.Storage;

namespace CamGuard.Tests.Common.Storage;

public class StorageKeysTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task IsManaged_GeneratedKeys_True()
    {
        var id = Guid.NewGuid();

        await Assert.That(StorageKeys.IsManaged(StorageKeys.Clip(12, id, StartedAt))).IsTrue();
        await Assert.That(StorageKeys.IsManaged(StorageKeys.Thumbnail(12, id, StartedAt))).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("nota.txt")]
    [Arguments("1/2026/10/04/0f8fad5bd9cb469fa16570867728950e.MP4")]
    [Arguments("1/2026/10/04/0f8fad5b-d9cb-469f-a165-70867728950e.mp4")]
    [Arguments("x/1/2026/10/04/0f8fad5bd9cb469fa16570867728950e.mp4")]
    [Arguments("1/2026/10/0f8fad5bd9cb469fa16570867728950e.jpg")]
    public async Task IsManaged_OtherKeys_False(string key)
    {
        await Assert.That(StorageKeys.IsManaged(key)).IsFalse();
    }
}
