using CamGuard.Api.Features.Tuya;

namespace CamGuard.Tests.Features.Tuya;

public class TuyaActivationWindowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 22, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Trigger_FirstEvent_StartsActivation()
    {
        var window = new TuyaActivationWindow(TimeSpan.FromSeconds(90));

        await Assert.That(window.Trigger(1, T0)).IsTrue();
        await Assert.That(window.IsActive(1, T0.AddSeconds(89))).IsTrue();
    }

    [Test]
    public async Task Trigger_DuringActivation_ExtendsWithoutRestarting()
    {
        var window = new TuyaActivationWindow(TimeSpan.FromSeconds(90));
        window.Trigger(1, T0);

        await Assert.That(window.Trigger(1, T0.AddSeconds(60))).IsFalse();
        await Assert.That(window.Expire(T0.AddSeconds(100))).IsEmpty();
        await Assert.That(window.Expire(T0.AddSeconds(150))).IsEquivalentTo([1]);
    }

    [Test]
    public async Task Expire_RemovesOnlyExpiredCameras()
    {
        var window = new TuyaActivationWindow(TimeSpan.FromSeconds(90));
        window.Trigger(1, T0);
        window.Trigger(2, T0.AddSeconds(60));

        await Assert.That(window.Expire(T0.AddSeconds(90))).IsEquivalentTo([1]);
        await Assert.That(window.IsActive(2, T0.AddSeconds(90))).IsTrue();
    }

    [Test]
    public async Task Trigger_AfterExpired_StartsAgain()
    {
        var window = new TuyaActivationWindow(TimeSpan.FromSeconds(90));
        window.Trigger(1, T0);

        await Assert.That(window.Trigger(1, T0.AddSeconds(91))).IsTrue();
    }

    [Test]
    public async Task Remove_ForgetsActivation()
    {
        var window = new TuyaActivationWindow(TimeSpan.FromSeconds(90));
        window.Trigger(1, T0);

        await Assert.That(window.Remove(1)).IsTrue();
        await Assert.That(window.Expire(T0.AddSeconds(200))).IsEmpty();
    }
}
