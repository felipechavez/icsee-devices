using CamGuard.Api.Common;
using CamGuard.Api.Features.Upload;
using Microsoft.Extensions.Options;

namespace CamGuard.Tests.Features.Upload;

public class UploadQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private string _dataDir = null!;
    private UploadQueue _queue = null!;

    [Before(Test)]
    public void Setup()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "camguard-tests", Guid.NewGuid().ToString("N"));
        _queue = new UploadQueue(Options.Create(new AppOptions { DataDirectory = _dataDir }));
    }

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    [Test]
    public async Task EnqueueAsync_JobIsDueImmediately()
    {
        var job = new UploadJob(Guid.NewGuid(), "clip.mp4", "thumb.jpg");

        await _queue.EnqueueAsync(job, CancellationToken.None);
        var due = await _queue.GetDueAsync(Now, CancellationToken.None);

        await Assert.That(due).Contains(job);
    }

    [Test]
    public async Task GetDueAsync_SkipsJobsScheduledInTheFuture()
    {
        var later = new UploadJob(Guid.NewGuid(), "a.mp4", null, Attempts: 1, NextAttemptAt: Now.AddMinutes(1));
        await _queue.SaveAsync(later, CancellationToken.None);

        var due = await _queue.GetDueAsync(Now, CancellationToken.None);

        await Assert.That(due).IsEmpty();
    }

    [Test]
    public async Task SaveAsync_SameRecording_OverwritesJob()
    {
        var id = Guid.NewGuid();
        await _queue.SaveAsync(new UploadJob(id, "a.mp4", null), CancellationToken.None);

        await _queue.SaveAsync(new UploadJob(id, "a.mp4", null, Attempts: 3, NextAttemptAt: Now), CancellationToken.None);
        var due = await _queue.GetDueAsync(Now, CancellationToken.None);

        await Assert.That(due).HasSingleItem();
        await Assert.That(due[0].Attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Remove_DeletesJob()
    {
        var id = Guid.NewGuid();
        await _queue.EnqueueAsync(new UploadJob(id, "a.mp4", null), CancellationToken.None);

        _queue.Remove(id);

        await Assert.That(await _queue.GetDueAsync(Now, CancellationToken.None)).IsEmpty();
    }

    [Test]
    public async Task Jobs_SurviveNewQueueInstance()
    {
        var job = new UploadJob(Guid.NewGuid(), "a.mp4", null);
        await _queue.EnqueueAsync(job, CancellationToken.None);

        var reopened = new UploadQueue(Options.Create(new AppOptions { DataDirectory = _dataDir }));

        await Assert.That(await reopened.GetDueAsync(Now, CancellationToken.None)).Contains(job);
    }

    [Test]
    public async Task GetDueAsync_OrdersByNextAttempt()
    {
        var first = new UploadJob(Guid.NewGuid(), "1.mp4", null, 1, Now.AddMinutes(-5));
        var second = new UploadJob(Guid.NewGuid(), "2.mp4", null, 1, Now.AddMinutes(-1));
        await _queue.SaveAsync(second, CancellationToken.None);
        await _queue.SaveAsync(first, CancellationToken.None);

        var due = await _queue.GetDueAsync(Now, CancellationToken.None);

        await Assert.That(due.Select(j => j.RecordingId)).IsEquivalentTo([first.RecordingId, second.RecordingId]);
        await Assert.That(due[0]).IsEqualTo(first);
    }
}
