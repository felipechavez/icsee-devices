using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Onvif;

namespace CamGuard.Tests.Features.Onvif;

public class OnvifMotionTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Update_MotionStarts_Awake()
    {
        var tracker = new OnvifMotionTracker(Delay);

        await Assert.That(tracker.Update(true, T0)).IsEqualTo(CameraEventType.Awake);
        await Assert.That(tracker.Update(true, T0.AddSeconds(1))).IsNull();
    }

    [Test]
    public async Task Update_MotionEnds_AsleepOnlyAfterDelay()
    {
        var tracker = new OnvifMotionTracker(Delay);
        tracker.Update(true, T0);

        await Assert.That(tracker.Update(false, T0.AddSeconds(35))).IsNull();
        await Assert.That(tracker.Tick(T0.AddSeconds(44))).IsNull();
        await Assert.That(tracker.TimeUntilAsleep(T0.AddSeconds(44))).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(tracker.Tick(T0.AddSeconds(45))).IsEqualTo(CameraEventType.Asleep);
        await Assert.That(tracker.IsAwake).IsFalse();
    }

    [Test]
    public async Task Update_FlickerWithinDelay_StaysAwake()
    {
        var tracker = new OnvifMotionTracker(Delay);
        tracker.Update(true, T0);
        tracker.Update(false, T0.AddSeconds(5));

        await Assert.That(tracker.Update(true, T0.AddSeconds(8))).IsNull();
        await Assert.That(tracker.Tick(T0.AddSeconds(20))).IsNull();
        await Assert.That(tracker.IsAwake).IsTrue();
    }

    [Test]
    public async Task Update_RepeatedFalse_KeepsFirstQuietTime()
    {
        var tracker = new OnvifMotionTracker(Delay);
        tracker.Update(true, T0);
        tracker.Update(false, T0.AddSeconds(1));

        await Assert.That(tracker.Update(false, T0.AddSeconds(11))).IsEqualTo(CameraEventType.Asleep);
    }

    [Test]
    public async Task Update_FalseWhileAsleep_NoEvent()
    {
        var tracker = new OnvifMotionTracker(Delay);

        await Assert.That(tracker.Update(false, T0)).IsNull();
        await Assert.That(tracker.TimeUntilAsleep(T0)).IsNull();
    }

    [Test]
    public async Task Disconnect_WhileAwake_Asleep()
    {
        var tracker = new OnvifMotionTracker(Delay);
        tracker.Update(true, T0);

        await Assert.That(tracker.Disconnect()).IsEqualTo(CameraEventType.Asleep);
        await Assert.That(tracker.Disconnect()).IsNull();
    }

    [Test]
    public async Task ZeroDelay_FalseSleepsImmediately()
    {
        var tracker = new OnvifMotionTracker(TimeSpan.Zero);
        tracker.Update(true, T0);

        await Assert.That(tracker.Update(false, T0.AddSeconds(1))).IsEqualTo(CameraEventType.Asleep);
    }
}
