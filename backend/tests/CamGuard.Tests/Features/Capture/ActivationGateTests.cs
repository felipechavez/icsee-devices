using CamGuard.Api.Features.Capture;

namespace CamGuard.Tests.Features.Capture;

public class ActivationGateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    [Test]
    public async Task TryStart_FirstActivation_Allowed()
    {
        var gate = new ActivationGate();

        await Assert.That(gate.TryStart(1, T0, Cooldown)).IsTrue();
        await Assert.That(gate.IsRecording(1)).IsTrue();
    }

    [Test]
    public async Task TryStart_WhileRecording_Rejected()
    {
        var gate = new ActivationGate();
        gate.TryStart(1, T0, Cooldown);

        await Assert.That(gate.TryStart(1, T0.AddMinutes(5), Cooldown)).IsFalse();
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(9, false)]
    [Arguments(10, true)]
    [Arguments(30, true)]
    public async Task TryStart_AfterFinish_RespectsCooldown(int secondsAfterFinish, bool expected)
    {
        var gate = new ActivationGate();
        gate.TryStart(1, T0, Cooldown);
        gate.Finish(1, T0.AddSeconds(60));

        var allowed = gate.TryStart(1, T0.AddSeconds(60 + secondsAfterFinish), Cooldown);

        await Assert.That(allowed).IsEqualTo(expected);
    }

    [Test]
    public async Task TryStart_ZeroCooldown_AllowsImmediately()
    {
        var gate = new ActivationGate();
        gate.TryStart(1, T0, Cooldown);
        gate.Finish(1, T0);

        await Assert.That(gate.TryStart(1, T0, TimeSpan.Zero)).IsTrue();
    }

    [Test]
    public async Task TryStart_DifferentCameras_AreIndependent()
    {
        var gate = new ActivationGate();
        gate.TryStart(1, T0, Cooldown);

        await Assert.That(gate.TryStart(2, T0, Cooldown)).IsTrue();
    }

    [Test]
    public async Task Finish_ClearsRecordingFlag()
    {
        var gate = new ActivationGate();
        gate.TryStart(1, T0, Cooldown);

        gate.Finish(1, T0.AddSeconds(5));

        await Assert.That(gate.IsRecording(1)).IsFalse();
    }
}
