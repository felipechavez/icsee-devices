using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Discovery;
using CamGuard.Api.Features.Tuya;

namespace CamGuard.Tests.Features.Discovery;

public class DiscoveryMergeTests
{
    private static readonly IReadOnlySet<string> Categories = new HashSet<string>(["sp", "videolock"], StringComparer.OrdinalIgnoreCase);

    private static Camera Icsee(string ip) => new() { Name = "c", Ip = ip, Go2rtcStream = "c", Kind = CameraKind.Icsee };

    private static Camera Tuya(string id, string? triggers = null) =>
        new() { Name = "t", Ip = "", Go2rtcStream = "t", Kind = CameraKind.Tuya, TuyaDeviceId = id, TuyaTriggerDeviceIds = triggers };

    [Test]
    public async Task Merge_RegisteredIp_Excluded()
    {
        var found = DiscoveryMerge.Merge(
            [new LanHit("192.168.1.10", LanProtocol.Dvrip, 34567), new LanHit("192.168.1.11", LanProtocol.Onvif, 80)],
            [], [Icsee("192.168.1.10")], Categories);

        await Assert.That(found.Count).IsEqualTo(1);
        await Assert.That(found[0].Ip).IsEqualTo("192.168.1.11");
        await Assert.That(found[0].Kind).IsEqualTo(CameraKind.Onvif);
        await Assert.That(found[0].SuggestedName).IsEqualTo("ONVIF 192.168.1.11");
    }

    [Test]
    public async Task Merge_DvripAndOnvifSameIp_IcseeWithDetail()
    {
        var found = DiscoveryMerge.Merge(
            [new LanHit("192.168.1.20", LanProtocol.Onvif, 8899), new LanHit("192.168.1.20", LanProtocol.Dvrip, 34567, "XM530")],
            [], [], Categories);

        await Assert.That(found.Count).IsEqualTo(1);
        await Assert.That(found[0].Kind).IsEqualTo(CameraKind.Icsee);
        await Assert.That(found[0].Port).IsEqualTo(34567);
        await Assert.That(found[0].Model).IsEqualTo("XM530");
        await Assert.That(found[0].Detail).Contains("8899");
    }

    [Test]
    public async Task Merge_OnvifSeveralPorts_Prefers80()
    {
        var found = DiscoveryMerge.Merge(
            [new LanHit("192.168.1.30", LanProtocol.Onvif, 8000), new LanHit("192.168.1.30", LanProtocol.Onvif, 80)],
            [], [], Categories);

        await Assert.That(found[0].Port).IsEqualTo(80);
    }

    [Test]
    public async Task Merge_Tuya_FiltersCategoryAndRegisteredIds()
    {
        var found = DiscoveryMerge.Merge([],
        [
            new TuyaListedDevice("cam000000001", "Patio", "Cámara", "sp", true),
            new TuyaListedDevice("lock00000001", "Puerta", "XOOMI C20", "videolock", false),
            new TuyaListedDevice("pir000000001", "Sensor", "PIR", "pir", true),
            new TuyaListedDevice("cam000000002", "Ya está", "Cámara", "sp", true),
            new TuyaListedDevice("cam000000003", "Disparador", "Cámara", "SP", true)
        ], [Tuya("cam000000002", "cam000000003")], Categories);

        await Assert.That(found.Select(f => f.TuyaDeviceId)).IsEquivalentTo(["cam000000001", "lock00000001"]);
        await Assert.That(found.All(f => f.Kind == CameraKind.Tuya)).IsTrue();
        await Assert.That(found.Single(f => f.TuyaDeviceId == "lock00000001").Detail).IsEqualTo("Fuera de línea");
        await Assert.That(found.Single(f => f.TuyaDeviceId == "lock00000001").Model).IsEqualTo("XOOMI C20");
    }

    [Test]
    public async Task Merge_Order_KindThenNumericIp()
    {
        var found = DiscoveryMerge.Merge(
        [
            new LanHit("192.168.1.100", LanProtocol.Onvif, 80),
            new LanHit("192.168.1.10", LanProtocol.Dvrip, 34567),
            new LanHit("192.168.1.9", LanProtocol.Dvrip, 34567)
        ], [new TuyaListedDevice("cam000000001", "A", null, "sp", true)], [], Categories);

        await Assert.That(string.Join(",", found.Select(f => f.Ip ?? f.TuyaDeviceId)))
            .IsEqualTo("192.168.1.9,192.168.1.10,192.168.1.100,cam000000001");
    }

    [Test]
    public async Task Merge_LongTuyaName_Truncated()
    {
        var found = DiscoveryMerge.Merge([], [new TuyaListedDevice("cam000000001", new string('x', 150), null, "sp", true)], [], Categories);

        await Assert.That(found[0].SuggestedName.Length).IsEqualTo(DiscoveryMerge.MaxNameLength);
    }
}
