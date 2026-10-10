using System.Text.Json.Nodes;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Devices;

namespace CamGuard.Tests.Features.Devices;

public class DeviceInfoParserTests
{
    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    [Test]
    public async Task ParseModel_SystemInfo_ReturnsHardware()
    {
        var json = Json("""{"Name":"SystemInfo","Ret":100,"SystemInfo":{"HardWare":"XM530_R80X20-PQ_8M ","SoftWareVersion":"V5.00"}}""");

        await Assert.That(DeviceInfoParser.ParseModel(json)).IsEqualTo("XM530_R80X20-PQ_8M");
    }

    [Test]
    public async Task ParseModel_Missing_Null()
    {
        await Assert.That(DeviceInfoParser.ParseModel(Json("""{"Ret":100}"""))).IsNull();
    }

    [Test]
    public async Task ParseStorage_HexPartitions_SumsInBytes()
    {
        // 0x7700 MB = 30464 MB en total, 0x6000 MB = 24576 MB libres.
        var json = Json("""
            {"Name":"StorageInfo","Ret":100,"StorageInfo":[
              {"PartNumber":1,"Partition":[{"TotalSpace":"0x00007700","RemainSpace":"0x00006000","Status":0}]}
            ]}
            """);

        var result = DeviceInfoParser.ParseStorage(json);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Total).IsEqualTo(30464L * 1024 * 1024);
        await Assert.That(result.Value.Used).IsEqualTo((30464L - 24576) * 1024 * 1024);
    }

    [Test]
    public async Task ParseStorage_NoCard_Null()
    {
        await Assert.That(DeviceInfoParser.ParseStorage(Json("""{"Ret":100,"StorageInfo":[]}"""))).IsNull();
    }

    [Test]
    [Arguments("""{"Ret":100,"Dev.ElectCapacity":{"Percent":78}}""", 78)]
    [Arguments("""{"Ret":100,"Dev.PowerInfo":[{"Electricity":"64"}]}""", 64)]
    [Arguments("""{"Ret":100,"Status.Battery":{"BatteryPercent":"91%"}}""", 91)]
    public async Task ParseBattery_KnownShapes_ReturnsPercent(string text, int expected)
    {
        await Assert.That(DeviceInfoParser.ParseBattery(Json(text))).IsEqualTo(expected);
    }

    [Test]
    public async Task ParseBattery_RealElectCapacityConfig_UsesPercentNotExtend()
    {
        // Respuesta real (config 1042) de una IPC_GK7201V300_LPG-G3-WQ.
        var json = Json("""
            {"Dev.ElectCapacity":{"DevStorageStatus":0,"electable":0,"extendElectable":3,"extendLevel":255,
              "extendPercent":-1,"freecapacity":0,"level":1,"percent":87},"Name":"Dev.ElectCapacity","Ret":100}
            """);

        await Assert.That(DeviceInfoParser.ParseBattery(json)).IsEqualTo(87);
    }

    [Test]
    [Arguments("""{"Ret":100,"Name":"Dev.ElectCapacity"}""")]
    [Arguments("""{"Ret":100,"Dev.ElectCapacity":{"Percent":250}}""")]
    public async Task ParseBattery_MissingOrImplausible_Null(string text)
    {
        await Assert.That(DeviceInfoParser.ParseBattery(Json(text))).IsNull();
    }

    [Test]
    [Arguments("0x10", 16L)]
    [Arguments("42", 42L)]
    [Arguments("x", null)]
    public async Task ParseNumber_Strings(string value, long? expected)
    {
        await Assert.That(DeviceInfoParser.ParseNumber(JsonValue.Create(value))).IsEqualTo(expected);
    }
}

public class SensitivityMapTests
{
    [Test]
    [Arguments(MotionSensitivity.Low)]
    [Arguments(MotionSensitivity.Medium)]
    [Arguments(MotionSensitivity.High)]
    public async Task ToLevel_FromLevel_RoundTrip(MotionSensitivity sensitivity)
    {
        await Assert.That(SensitivityMap.FromLevel(SensitivityMap.ToLevel(sensitivity))).IsEqualTo(sensitivity);
    }

    [Test]
    public async Task WithLevel_ArrayPerChannel_ChangesEveryLevel()
    {
        var config = JsonNode.Parse("""[{"Enable":true,"Level":3},{"Enable":false,"Level":1}]""");

        var updated = SensitivityMap.WithLevel(config, 6)!.AsArray();

        await Assert.That(updated[0]!["Level"]!.GetValue<int>()).IsEqualTo(6);
        await Assert.That(updated[1]!["Level"]!.GetValue<int>()).IsEqualTo(6);
        await Assert.That(config![0]!["Level"]!.GetValue<int>()).IsEqualTo(3);
    }

    [Test]
    public async Task WithLevel_ObjectWithoutLevel_Null()
    {
        await Assert.That(SensitivityMap.WithLevel(JsonNode.Parse("""{"Enable":true}"""), 4)).IsNull();
    }
}
