using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Tuya;

namespace CamGuard.Tests.Features.Tuya;

public class TuyaEventServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 3, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task IsStale_RecentEvent_False()
    {
        await Assert.That(TuyaEventService.IsStale(Now.AddSeconds(-30), Now)).IsFalse();
    }

    [Test]
    public async Task IsStale_EventFromWhileOffline_True()
    {
        await Assert.That(TuyaEventService.IsStale(Now.AddMinutes(-10), Now)).IsTrue();
    }

    [Test]
    public async Task IsStale_WithoutTime_False()
    {
        await Assert.That(TuyaEventService.IsStale(null, Now)).IsFalse();
    }

    [Test]
    [Arguments(DetectionMode.Filter, TuyaSignalKind.Motion, false)]
    [Arguments(DetectionMode.Tag, TuyaSignalKind.Motion, true)]
    [Arguments(DetectionMode.Off, TuyaSignalKind.Motion, true)]
    [Arguments(DetectionMode.Filter, TuyaSignalKind.Doorbell, true)]
    [Arguments(DetectionMode.Filter, TuyaSignalKind.Alarm, true)]
    [Arguments(DetectionMode.Filter, TuyaSignalKind.Unlock, true)]
    public async Task ShouldNotifyImmediately_FilterOnlyMutesMotion(DetectionMode mode, TuyaSignalKind kind, bool expected)
    {
        var camera = Tuya(1, "lock0000001", null);
        camera.DetectionMode = mode;

        await Assert.That(TuyaEventService.ShouldNotifyImmediately(camera, new TuyaSignal(kind))).IsEqualTo(expected);
    }

    private static Camera Tuya(int id, string device, string? triggers) =>
        new() { Id = id, Name = $"cam{id}", Ip = "", Go2rtcStream = $"cam{id}", Kind = CameraKind.Tuya, TuyaDeviceId = device, TuyaTriggerDeviceIds = triggers };

    [Test]
    public async Task BuildTriggerMap_SensorLinkedToSeveralCameras_MapsAll()
    {
        var map = TuyaEventService.BuildTriggerMap([
            Tuya(1, "lock0000001", "sensor00001"),
            Tuya(2, "cam00000002", "sensor00001, sensor00002"),
            Tuya(3, "cam00000003", null)
        ]);

        await Assert.That(map["sensor00001"].Select(c => c.Id)).IsEquivalentTo([1, 2]);
        await Assert.That(map["sensor00002"].Select(c => c.Id)).IsEquivalentTo([2]);
        await Assert.That(map.Count).IsEqualTo(2);
    }
}