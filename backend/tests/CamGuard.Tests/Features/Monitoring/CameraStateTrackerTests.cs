using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Monitoring;

namespace CamGuard.Tests.Features.Monitoring;

public class CameraStateTrackerTests
{
    [Test]
    public async Task Update_PortOpensWhileAsleep_EmitsAwake()
    {
        var tracker = new CameraStateTracker(asleepAfterFailures: 3);

        var change = tracker.Update(portOpen: true);

        await Assert.That(change).IsEqualTo(CameraEventType.Awake);
        await Assert.That(tracker.IsAwake).IsTrue();
    }

    [Test]
    public async Task Update_PortClosedAtStartup_EmitsNothing()
    {
        var tracker = new CameraStateTracker(asleepAfterFailures: 3);

        var change = tracker.Update(portOpen: false);

        await Assert.That(change).IsNull();
        await Assert.That(tracker.IsAwake).IsFalse();
    }

    [Test]
    public async Task Update_PortStaysOpen_EmitsAwakeOnlyOnce()
    {
        var tracker = new CameraStateTracker(asleepAfterFailures: 3);
        tracker.Update(true);

        var change = tracker.Update(true);

        await Assert.That(change).IsNull();
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(5)]
    public async Task Update_ConsecutiveFailures_EmitsAsleepAfterThreshold(int threshold)
    {
        var tracker = new CameraStateTracker(threshold);
        tracker.Update(true);

        var changes = Enumerable.Range(0, threshold).Select(_ => tracker.Update(false)).ToList();

        await Assert.That(changes.Take(threshold - 1).All(c => c is null)).IsTrue();
        await Assert.That(changes[^1]).IsEqualTo(CameraEventType.Asleep);
        await Assert.That(tracker.IsAwake).IsFalse();
    }

    [Test]
    public async Task Update_SuccessBetweenFailures_ResetsFailureCount()
    {
        var tracker = new CameraStateTracker(asleepAfterFailures: 3);
        tracker.Update(true);
        tracker.Update(false);
        tracker.Update(false);
        tracker.Update(true);

        var afterTwoMoreFailures = new[] { tracker.Update(false), tracker.Update(false) };

        await Assert.That(afterTwoMoreFailures.All(c => c is null)).IsTrue();
        await Assert.That(tracker.IsAwake).IsTrue();
    }

    [Test]
    public async Task Update_WakesAgainAfterSleeping_EmitsAwake()
    {
        var tracker = new CameraStateTracker(asleepAfterFailures: 1);
        tracker.Update(true);
        tracker.Update(false);

        var change = tracker.Update(true);

        await Assert.That(change).IsEqualTo(CameraEventType.Awake);
    }

    [Test]
    public async Task Update_ThresholdZero_TreatedAsOne()
    {
        var tracker = new CameraStateTracker(asleepAfterFailures: 0);
        tracker.Update(true);

        var change = tracker.Update(false);

        await Assert.That(change).IsEqualTo(CameraEventType.Asleep);
    }
}
