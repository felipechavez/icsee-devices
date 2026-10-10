using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Recordings;

namespace CamGuard.Tests.Features.Recordings;

public class ListRecordingsTests
{
    [Test]
    [Arguments(null, null, 1, ListRecordings.DefaultPageSize)]
    [Arguments(0, 10, 1, 10)]
    [Arguments(-4, 500, 1, ListRecordings.MaxPageSize)]
    [Arguments(3, 0, 3, 1)]
    public async Task NormalizePaging_ClampsValues(int? page, int? pageSize, int expectedPage, int expectedSize)
    {
        var (p, size) = ListRecordings.NormalizePaging(page, pageSize);

        await Assert.That(p).IsEqualTo(expectedPage);
        await Assert.That(size).IsEqualTo(expectedSize);
    }
}

public class GetPlaybackUrlTests
{
    [Test]
    public async Task DownloadFileName_UsesCameraAndUtcTimestamp()
    {
        var recording = new Recording
        {
            CameraId = 1,
            StartedAt = new DateTimeOffset(2026, 10, 4, 7, 5, 9, TimeSpan.FromHours(-5))
        };

        await Assert.That(GetPlaybackUrl.DownloadFileName(recording)).IsEqualTo("camguard-1-20261004-120509.mp4");
    }
}

public class RecordingTimelineTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 7, 0, 0, 0, TimeSpan.FromHours(-5));

    [Test]
    public async Task Validate_OneDay_Accepted()
    {
        await Assert.That(RecordingTimeline.Validate(Day, Day.AddDays(1))).IsEmpty();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(3)]
    public async Task Validate_EmptyReversedOrTooLong_Rejected(int days)
    {
        await Assert.That(RecordingTimeline.Validate(Day, Day.AddDays(days))).IsNotEmpty();
    }
}
