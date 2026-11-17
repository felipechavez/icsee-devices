using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Health;

namespace CamGuard.Tests.Features.Health;

public class HealthEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly HealthThresholds Thresholds = new(20, 48, 30, 1024, 10);
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static CameraHealth Camera(int? battery = 80, double? daysLeft = null, bool enabled = true, DateTimeOffset? lastAwake = null) =>
        new(1, "Entrada", CameraKind.Icsee, enabled, battery, Now.AddHours(-1), daysLeft, [], lastAwake ?? Now.AddHours(-1), 3);

    private static HealthFacts Facts(
        CameraHealth? camera = null,
        IReadOnlyList<ServiceHealth>? services = null,
        UploadsHealth? uploads = null,
        DiskHealth? disk = null) =>
        new([camera ?? Camera()], services ?? [], uploads ?? new UploadsHealth(0, 0, null), disk ?? new DiskHealth(10L << 30, 100L << 30),
            new StorageUsage(0, 0, 0), Now.AddDays(-10));

    private static IReadOnlyList<string> Keys(HealthFacts facts, IReadOnlySet<string>? open = null) =>
        HealthEvaluator.Evaluate(facts, Thresholds, open ?? None, Now).Select(i => i.Key).ToList();

    [Test]
    public async Task Evaluate_AllHealthy_NoIssues()
    {
        await Assert.That(Keys(Facts())).IsEmpty();
    }

    [Test]
    public async Task Evaluate_BatteryBelowThreshold_Opens()
    {
        await Assert.That(Keys(Facts(Camera(battery: 15)))).IsEquivalentTo(["battery:1"]);
    }

    [Test]
    public async Task Evaluate_BatteryInHysteresisBandWhileOpen_StaysOpen()
    {
        var open = new HashSet<string> { "battery:1" };

        await Assert.That(Keys(Facts(Camera(battery: 22)), open)).IsEquivalentTo(["battery:1"]);
        await Assert.That(Keys(Facts(Camera(battery: 22)))).IsEmpty();
        await Assert.That(Keys(Facts(Camera(battery: 25)), open)).IsEmpty();
    }

    [Test]
    public async Task Evaluate_ForecastRunningOut_OpensAboveThreshold()
    {
        await Assert.That(Keys(Facts(Camera(battery: 40, daysLeft: 2.5)))).IsEquivalentTo(["battery:1"]);
        await Assert.That(Keys(Facts(Camera(battery: 40, daysLeft: 5)))).IsEmpty();
    }

    [Test]
    public async Task Evaluate_NoBatteryInfo_NoBatteryIssue()
    {
        await Assert.That(Keys(Facts(Camera(battery: null)))).IsEmpty();
    }

    [Test]
    public async Task Evaluate_SilentCamera_Opens()
    {
        await Assert.That(Keys(Facts(Camera(lastAwake: Now.AddHours(-49))))).IsEquivalentTo(["silent:1"]);
    }

    [Test]
    public async Task Evaluate_OnvifQuietForDays_NotSilent()
    {
        var camera = Camera(battery: null, lastAwake: Now.AddDays(-5)) with { Kind = CameraKind.Onvif, Connected = true, ConnectionSince = Now.AddDays(-5) };

        await Assert.That(Keys(Facts(camera))).IsEmpty();
    }

    [Test]
    public async Task Evaluate_OnvifOfflinePastGrace_Opens()
    {
        var camera = Camera(battery: null) with { Kind = CameraKind.Onvif, Connected = false, ConnectionSince = Now.AddMinutes(-11) };

        await Assert.That(Keys(Facts(camera))).IsEquivalentTo(["offline:1"]);
        await Assert.That(Keys(Facts(camera with { ConnectionSince = Now.AddMinutes(-5) }))).IsEmpty();
    }

    [Test]
    public async Task Evaluate_OnvifNotTriedYet_NoIssue()
    {
        var camera = Camera(battery: null) with { Kind = CameraKind.Onvif, Connected = null, ConnectionSince = null };

        await Assert.That(Keys(Facts(camera))).IsEmpty();
    }

    [Test]
    public async Task Evaluate_DisabledCamera_Ignored()
    {
        await Assert.That(Keys(Facts(Camera(battery: 5, enabled: false, lastAwake: Now.AddDays(-5))))).IsEmpty();
    }

    [Test]
    public async Task Evaluate_CameraNeverAwake_CountsFromMonitorStart()
    {
        var camera = Camera() with { LastAwakeAt = null };
        var facts = Facts(camera) with { MonitorStartedAt = Now.AddHours(-1) };

        await Assert.That(Keys(facts)).IsEmpty();
        await Assert.That(Keys(facts with { MonitorStartedAt = Now.AddHours(-50) })).IsEquivalentTo(["silent:1"]);
    }

    [Test]
    [Arguments(5, false)]
    [Arguments(15, true)]
    public async Task Evaluate_ServiceDown_OpensAfterGrace(int minutesDown, bool expected)
    {
        var go2rtc = new ServiceHealth(HealthEvaluator.Go2rtc, false, Now.AddMinutes(-minutesDown), "connection refused");

        var keys = Keys(Facts(services: [go2rtc]));

        await Assert.That(keys.Contains("service:go2rtc")).IsEqualTo(expected);
    }

    [Test]
    public async Task Evaluate_ServiceOk_NoIssue()
    {
        var tuya = new ServiceHealth(HealthEvaluator.Tuya, true, Now.AddDays(-1), null);

        await Assert.That(Keys(Facts(services: [tuya]))).IsEmpty();
    }

    [Test]
    public async Task Evaluate_UploadsStuck_Opens()
    {
        await Assert.That(Keys(Facts(uploads: new UploadsHealth(2, 0, Now.AddMinutes(-31))))).IsEquivalentTo(["queue"]);
        await Assert.That(Keys(Facts(uploads: new UploadsHealth(2, 0, Now.AddMinutes(-5))))).IsEmpty();
    }

    [Test]
    public async Task Evaluate_LowDisk_Opens()
    {
        await Assert.That(Keys(Facts(disk: new DiskHealth(500L << 20, 100L << 30)))).IsEquivalentTo(["disk"]);
    }

    [Test]
    public async Task Evaluate_NtfyDown_NoIssue()
    {
        var ntfy = new ServiceHealth(HealthEvaluator.Ntfy, false, Now.AddHours(-3), "timeout");

        await Assert.That(Keys(Facts(services: [ntfy]))).IsEmpty();
    }

    private static BackupHealth HealthyBackup => new(Now.AddHours(-9), Now.AddHours(-9), true, null);

    [Test]
    public async Task Evaluate_BackupsNull_NotChecked()
    {
        await Assert.That(Keys(Facts() with { Backups = null })).IsEmpty();
    }

    [Test]
    public async Task Evaluate_BackupHealthy_NoIssue()
    {
        await Assert.That(Keys(Facts() with { Backups = HealthyBackup })).IsEmpty();
    }

    [Test]
    public async Task Evaluate_NoBackup_Opens()
    {
        await Assert.That(Keys(Facts() with { Backups = new BackupHealth(null, null, null, null) })).IsEquivalentTo(["backup"]);
    }

    [Test]
    public async Task Evaluate_OldBackup_Opens()
    {
        var backups = HealthyBackup with { LatestAt = Now.AddHours(-37) };

        await Assert.That(Keys(Facts() with { Backups = backups })).IsEquivalentTo(["backup"]);
    }

    [Test]
    public async Task Evaluate_BackupVerifyFailed_OpensWithDetail()
    {
        var backups = HealthyBackup with { VerifyOk = false, VerifyDetail = "gzip: archivo truncado" };

        var issues = HealthEvaluator.Evaluate(Facts() with { Backups = backups }, Thresholds, None, Now);

        await Assert.That(issues.Select(i => i.Key)).IsEquivalentTo(["backup-verify"]);
        await Assert.That(issues[0].Title).IsEqualTo("Falló la verificación del respaldo");
        await Assert.That(issues[0].Detail).IsEqualTo("gzip: archivo truncado");
    }

    [Test]
    public async Task Evaluate_BackupVerifyOldOrMissing_Opens()
    {
        var old = HealthyBackup with { VerifiedAt = Now.AddDays(-9) };
        var never = HealthyBackup with { VerifiedAt = null, VerifyOk = null };

        await Assert.That(Keys(Facts() with { Backups = old })).IsEquivalentTo(["backup-verify"]);
        await Assert.That(Keys(Facts() with { Backups = never })).IsEquivalentTo(["backup-verify"]);
    }

    [Test]
    [Arguments(5_000_000_000L, 0)]
    [Arguments(30_000_000_000L, 0.3)]
    public async Task EstimatedMonthlyUsd_AppliesFreeTier(long bytes, double expected)
    {
        await Assert.That(HealthEvaluator.EstimatedMonthlyUsd(bytes, 10, 0.015)).IsEqualTo(expected);
    }
}
