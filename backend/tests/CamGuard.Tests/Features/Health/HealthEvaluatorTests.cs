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
    [Arguments(5_000_000_000L, 0)]
    [Arguments(30_000_000_000L, 0.3)]
    public async Task EstimatedMonthlyUsd_AppliesFreeTier(long bytes, double expected)
    {
        await Assert.That(HealthEvaluator.EstimatedMonthlyUsd(bytes, 10, 0.015)).IsEqualTo(expected);
    }
}
