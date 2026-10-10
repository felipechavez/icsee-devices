using CamGuard.Api.Common.Data;
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
    public async Task ExpiredBefore_OnlyOldFinishedClips(RecordingStatus status, int daysAgo, bool expected)
    {
        var cutoff = RetentionPolicy.Cutoff(Now, 30);
        var recording = new Recording { StartedAt = Now.AddDays(daysAgo), Status = status };

        var expired = RetentionPolicy.ExpiredBefore(cutoff).Compile()(recording);

        await Assert.That(expired).IsEqualTo(expected);
    }
}
