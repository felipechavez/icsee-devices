using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Storage;
using CamGuard.Api.Features.Retention;

namespace CamGuard.Tests.Features.Retention;

public class RetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(30)]
    [Arguments(1)]
    public async Task Cutoff_SubtractsRetentionDays(int days)
    {
        await Assert.That(RetentionPolicy.Cutoff(Now, days)).IsEqualTo(Now.AddDays(-days));
    }

    [Test]
    [Arguments(0)]
    [Arguments(-5)]
    public async Task Cutoff_InvalidDays_KeepsAtLeastOneDay(int days)
    {
        await Assert.That(RetentionPolicy.Cutoff(Now, days)).IsEqualTo(Now.AddDays(-1));
    }

    [Test]
    [Arguments(RecordingStatus.Uploaded, -31, true)]
    [Arguments(RecordingStatus.Failed, -31, true)]
    [Arguments(RecordingStatus.Uploaded, -29, false)]
    [Arguments(RecordingStatus.Recording, -31, false)]
    [Arguments(RecordingStatus.Uploading, -31, false)]
    [Arguments(RecordingStatus.Analyzing, -31, false)]
    [Arguments(RecordingStatus.Discarded, -31, true)]
    [Arguments(RecordingStatus.Discarded, -29, false)]
    public async Task ExpiredBefore_OnlyOldFinishedClips(RecordingStatus status, int daysAgo, bool expected)
    {
        var cutoff = RetentionPolicy.Cutoff(Now, 30);
        var recording = new Recording { StartedAt = Now.AddDays(daysAgo), Status = status };

        var expired = RetentionPolicy.ExpiredBefore(cutoff).Compile()(recording);

        await Assert.That(expired).IsEqualTo(expected);
    }

    private static readonly Guid Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Test]
    public async Task IsOrphanExpired_OldManagedKey_True()
    {
        var cutoff = RetentionPolicy.Cutoff(Now, 30);
        var startedAt = Now.AddDays(-31);

        await Assert.That(RetentionPolicy.IsOrphanExpired(StorageKeys.Clip(1, Id, startedAt), startedAt, cutoff)).IsTrue();
        await Assert.That(RetentionPolicy.IsOrphanExpired(StorageKeys.Thumbnail(1, Id, startedAt), startedAt, cutoff)).IsTrue();
    }

    [Test]
    public async Task IsOrphanExpired_RecentKey_False()
    {
        var cutoff = RetentionPolicy.Cutoff(Now, 30);
        var startedAt = Now.AddDays(-29);

        await Assert.That(RetentionPolicy.IsOrphanExpired(StorageKeys.Clip(1, Id, startedAt), startedAt, cutoff)).IsFalse();
    }

    [Test]
    [Arguments("otra/cosa.mp4")]
    [Arguments("1/2026/10/04/no-guid.mp4")]
    [Arguments("1/2026/10/04/0f8fad5bd9cb469fa16570867728950e.txt")]
    public async Task IsOrphanExpired_ForeignKey_False(string key)
    {
        var cutoff = RetentionPolicy.Cutoff(Now, 30);

        await Assert.That(RetentionPolicy.IsOrphanExpired(key, Now.AddDays(-100), cutoff)).IsFalse();
    }
}
