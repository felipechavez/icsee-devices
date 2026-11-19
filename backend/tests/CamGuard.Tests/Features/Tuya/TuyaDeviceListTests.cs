using System.Text.Json.Nodes;
using CamGuard.Api.Features.Tuya;

namespace CamGuard.Tests.Features.Tuya;

public class TuyaDeviceListTests
{
    [Test]
    public async Task ParseDeviceList_AssociatedUsersPage_DevicesAndNextKey()
    {
        var page = TuyaApiClient.ParseDeviceList(JsonNode.Parse("""
            {
              "devices": [
                { "id": "eb8d8e837c0a688a86f1kb", "name": "Puerta", "product_name": "XOOMI C20", "category": "videolock", "online": true, "local_key": "secreto" },
                { "id": "", "name": "sin id" },
                { "name": "tampoco" }
              ],
              "has_more": true,
              "last_row_key": "ABC123"
            }
            """));

        await Assert.That(page.Devices.Count).IsEqualTo(1);
        await Assert.That(page.Devices[0]).IsEqualTo(new TuyaListedDevice("eb8d8e837c0a688a86f1kb", "Puerta", "XOOMI C20", "videolock", true));
        await Assert.That(page.NextRowKey).IsEqualTo("ABC123");
    }

    [Test]
    public async Task ParseDeviceList_LastPage_NoNextKey()
    {
        var page = TuyaApiClient.ParseDeviceList(JsonNode.Parse("""{ "devices": [], "has_more": false, "last_row_key": "ABC123" }"""));

        await Assert.That(page.Devices.Count).IsEqualTo(0);
        await Assert.That(page.NextRowKey).IsNull();
    }

    [Test]
    public async Task ParseDeviceList_UserDevicesArray_NameDefaultsToId()
    {
        var page = TuyaApiClient.ParseDeviceList(JsonNode.Parse("""[{ "id": "cam000000001", "category": "sp" }]"""));

        await Assert.That(page.Devices[0].Name).IsEqualTo("cam000000001");
        await Assert.That(page.Devices[0].Online).IsFalse();
        await Assert.That(page.NextRowKey).IsNull();
    }

    [Test]
    public async Task ParseDeviceList_Null_Empty()
    {
        await Assert.That(TuyaApiClient.ParseDeviceList(null).Devices.Count).IsEqualTo(0);
    }
}
