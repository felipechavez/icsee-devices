using CamGuard.Api.Features.Monitoring;

namespace CamGuard.Tests.Features.Monitoring;

public class CameraStateStoreTests
{
    [Test]
    public async Task Get_UnknownCamera_ReturnsAsleepWithoutTimestamp()
    {
        var store = new CameraStateStore();

        var state = store.Get(42);

        await Assert.That(state).IsEqualTo(new CameraState(false, null));
    }

    [Test]
    public async Task Set_ThenGet_ReturnsLatestState()
    {
        var store = new CameraStateStore();
        var at = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        store.Set(1, true, at);

        await Assert.That(store.Get(1)).IsEqualTo(new CameraState(true, at));
    }

    [Test]
    public async Task Remove_KnownCamera_ForgetsState()
    {
        var store = new CameraStateStore();
        store.Set(1, true, new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));

        store.Remove(1);

        await Assert.That(store.Get(1)).IsEqualTo(new CameraState(false, null));
    }
}
