using CamGuard.Api.Features.Streams;

namespace CamGuard.Tests.Features.Streams;

public class StreamSyncPlanTests
{
    private static readonly Dictionary<string, string> NoneApplied = new();

    [Test]
    public async Task Compute_MissingStream_IsPatched()
    {
        var desired = new Dictionary<string, string> { ["patio"] = "dvrip://a" };

        var plan = StreamSyncPlan.Compute(desired, new Dictionary<string, string?>(), NoneApplied);

        await Assert.That(plan.ToPatch).IsEquivalentTo([new KeyValuePair<string, string>("patio", "dvrip://a")]);
        await Assert.That(plan.ToDelete).IsEmpty();
    }

    [Test]
    public async Task Compute_AppliedAndUnchanged_NothingToDo()
    {
        var desired = new Dictionary<string, string> { ["patio"] = "dvrip://a" };
        // Con la conexión abierta go2rtc no informa el origen.
        var current = new Dictionary<string, string?> { ["patio"] = null };

        var plan = StreamSyncPlan.Compute(desired, current, desired);

        await Assert.That(plan.ToPatch).IsEmpty();
        await Assert.That(plan.ToDelete).IsEmpty();
    }

    [Test]
    public async Task Compute_ReportedSourceMatches_NothingToDo()
    {
        var desired = new Dictionary<string, string> { ["patio"] = "dvrip://a" };
        var current = new Dictionary<string, string?> { ["patio"] = "dvrip://a" };

        var plan = StreamSyncPlan.Compute(desired, current, NoneApplied);

        await Assert.That(plan.ToPatch).IsEmpty();
    }

    [Test]
    public async Task Compute_SourceChanged_IsPatched()
    {
        var desired = new Dictionary<string, string> { ["patio"] = "dvrip://nueva" };
        var current = new Dictionary<string, string?> { ["patio"] = null };
        var applied = new Dictionary<string, string> { ["patio"] = "dvrip://vieja" };

        var plan = StreamSyncPlan.Compute(desired, current, applied);

        await Assert.That(plan.ToPatch).IsEquivalentTo([new KeyValuePair<string, string>("patio", "dvrip://nueva")]);
    }

    [Test]
    public async Task Compute_UnknownSourceAfterRestart_IsPatched()
    {
        var desired = new Dictionary<string, string> { ["patio"] = "dvrip://a" };
        var current = new Dictionary<string, string?> { ["patio"] = null };

        var plan = StreamSyncPlan.Compute(desired, current, NoneApplied);

        await Assert.That(plan.ToPatch).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Compute_StreamWithoutCamera_IsDeleted()
    {
        var desired = new Dictionary<string, string> { ["patio"] = "dvrip://a" };
        var current = new Dictionary<string, string?> { ["patio"] = "dvrip://a", ["vieja"] = null };

        var plan = StreamSyncPlan.Compute(desired, current, NoneApplied);

        await Assert.That(plan.ToDelete).IsEquivalentTo(["vieja"]);
    }

    [Test]
    public async Task Compute_StreamManagedElsewhere_IsKeptAndNotPatched()
    {
        var desired = new Dictionary<string, string> { ["patio"] = "dvrip://a" };
        var current = new Dictionary<string, string?> { ["patio"] = "dvrip://a", ["chapa"] = "rtsps://tuya/x" };

        var plan = StreamSyncPlan.Compute(desired, current, NoneApplied, new HashSet<string> { "chapa" });

        await Assert.That(plan.ToDelete).IsEmpty();
        await Assert.That(plan.ToPatch).IsEmpty();
    }
}
