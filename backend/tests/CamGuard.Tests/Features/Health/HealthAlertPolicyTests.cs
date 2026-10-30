using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Health;

namespace CamGuard.Tests.Features.Health;

public class HealthAlertPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Repeat = TimeSpan.FromHours(24);

    private static HealthIssue Issue(string key) => new(key, HealthSeverity.Warning, key, null, null, Now);

    private static Dictionary<string, HealthAlert> Open(string key, DateTimeOffset? lastNotifiedAt) =>
        new() { [key] = new HealthAlert { Key = key, Title = key, OpenedAt = Now.AddDays(-2), LastNotifiedAt = lastNotifiedAt } };

    [Test]
    public async Task Decide_NewIssue_OpensAndNotifies()
    {
        var d = HealthAlertPolicy.Decide([Issue("disk")], new Dictionary<string, HealthAlert>(), Now, Repeat);

        await Assert.That(d.ToOpen.Select(i => i.Key)).IsEquivalentTo(["disk"]);
        await Assert.That(d.ToNotify.Select(i => i.Key)).IsEquivalentTo(["disk"]);
        await Assert.That(d.ToResolve).IsEmpty();
    }

    [Test]
    public async Task Decide_OpenRecentlyNotified_DoesNotRepeat()
    {
        var d = HealthAlertPolicy.Decide([Issue("disk")], Open("disk", Now.AddHours(-23)), Now, Repeat);

        await Assert.That(d.ToOpen).IsEmpty();
        await Assert.That(d.ToNotify).IsEmpty();
    }

    [Test]
    public async Task Decide_OpenNotifiedLongAgo_Repeats()
    {
        var d = HealthAlertPolicy.Decide([Issue("disk")], Open("disk", Now.AddHours(-24)), Now, Repeat);

        await Assert.That(d.ToNotify.Select(i => i.Key)).IsEquivalentTo(["disk"]);
    }

    [Test]
    public async Task Decide_OpenNeverNotified_RetriesNotification()
    {
        var d = HealthAlertPolicy.Decide([Issue("disk")], Open("disk", null), Now, Repeat);

        await Assert.That(d.ToOpen).IsEmpty();
        await Assert.That(d.ToNotify.Select(i => i.Key)).IsEquivalentTo(["disk"]);
    }

    [Test]
    public async Task Decide_IssueGone_Resolves()
    {
        var d = HealthAlertPolicy.Decide([], Open("disk", Now.AddHours(-1)), Now, Repeat);

        await Assert.That(d.ToResolve.Select(a => a.Key)).IsEquivalentTo(["disk"]);
        await Assert.That(HealthAlertPolicy.NotifyResolution(d.ToResolve[0])).IsTrue();
    }

    [Test]
    public async Task NotifyResolution_NeverNotified_False()
    {
        var alert = new HealthAlert { Key = "disk", Title = "disk", OpenedAt = Now, LastNotifiedAt = null };

        await Assert.That(HealthAlertPolicy.NotifyResolution(alert)).IsFalse();
    }
}
