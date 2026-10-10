using System.Globalization;
using System.Text.Json.Nodes;
using CamGuard.Api.Common.Data;

namespace CamGuard.Api.Features.Devices;

/// <summary>Estado leído de la cámara; cada dato es null si el firmware no lo entrega.</summary>
public record DeviceSnapshot(int? BatteryPercent, long? StorageUsedBytes, long? StorageTotalBytes, string? Model);

/// <summary>
/// Nombres DVRIP consultados. Las claves de batería y detección cambian según el firmware XM:
/// se prueban en orden y se usa la primera que responde (ver poc/README.md, "Datos DVRIP").
/// </summary>
public static class DvripQueries
{
    public const string SystemInfo = "SystemInfo";
    public const string StorageInfo = "StorageInfo";

    public static readonly string[] Battery = ["Dev.ElectCapacity", "Dev.PowerInfo", "Status.Battery", "Dev.BatteryInfo"];

    public static readonly string[] Detection = ["Detect.PIRDetect", "Detect.PirDetect", "Detect.MotionDetect"];
}

/// <summary>Interpreta las respuestas JSON de DVRIP. Sin E/S: se prueba con respuestas capturadas.</summary>
public static class DeviceInfoParser
{
    private static readonly string[] BatteryKeys = ["Percent", "ElectCapacity", "BatteryPercent", "Capacity", "Battery", "Electricity"];

    /// <summary>"HardWare" de SystemInfo, p. ej. "XM530_R80X20-PQ_8M".</summary>
    public static string? ParseModel(JsonObject systemInfo)
    {
        var hardware = (systemInfo[DvripQueries.SystemInfo] as JsonObject)?["HardWare"]?.GetValue<string>()?.Trim();
        return string.IsNullOrEmpty(hardware) ? null : hardware;
    }

    /// <summary>
    /// Suma las particiones de StorageInfo. TotalSpace y RemainSpace vienen en MB, en hex ("0x00007700") o número.
    /// Sin SD (sin particiones o total 0) devuelve null.
    /// </summary>
    public static (long Used, long Total)? ParseStorage(JsonObject storageInfo)
    {
        if (storageInfo[DvripQueries.StorageInfo] is not JsonArray disks) return null;

        long totalMb = 0, remainMb = 0;
        foreach (var disk in disks.OfType<JsonObject>())
        {
            if (disk["Partition"] is not JsonArray partitions) continue;
            foreach (var p in partitions.OfType<JsonObject>())
            {
                totalMb += ParseNumber(p["TotalSpace"]) ?? 0;
                remainMb += ParseNumber(p["RemainSpace"]) ?? 0;
            }
        }

        if (totalMb <= 0) return null;
        const long mb = 1024 * 1024;
        return (Math.Max(0, totalMb - remainMb) * mb, totalMb * mb);
    }

    /// <summary>Busca en la respuesta el primer valor de batería plausible (0–100).</summary>
    public static int? ParseBattery(JsonNode? response)
    {
        switch (response)
        {
            case JsonObject obj:
                // El firmware no respeta mayúsculas: Dev.ElectCapacity trae "percent".
                foreach (var key in BatteryKeys)
                {
                    var match = obj.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (match.Value is JsonValue v && ParseNumber(v) is { } n && n is >= 0 and <= 100)
                        return (int)n;
                }
                foreach (var (key, child) in obj)
                {
                    if (key is "Ret" or "SessionID" or "Name") continue;
                    if (ParseBattery(child) is { } found) return found;
                }
                return null;
            case JsonArray array:
                return array.Select(ParseBattery).FirstOrDefault(n => n is not null);
            default:
                return null;
        }
    }

    /// <summary>Número JSON, cadena decimal o cadena hex "0x…".</summary>
    public static long? ParseNumber(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        if (!v.TryGetValue<string>(out var s)) return null;

        s = s.Trim().TrimEnd('%');
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec) ? dec : null;
    }
}

/// <summary>Baja/Media/Alta ↔ "Level" de detección del firmware XM (1 = mínima, 6 = máxima).</summary>
public static class SensitivityMap
{
    public static int ToLevel(MotionSensitivity s) => s switch
    {
        MotionSensitivity.Low => 2,
        MotionSensitivity.High => 6,
        _ => 4
    };

    public static MotionSensitivity FromLevel(int level) => level switch
    {
        <= 2 => MotionSensitivity.Low,
        >= 5 => MotionSensitivity.High,
        _ => MotionSensitivity.Medium
    };

    /// <summary>
    /// Copia de la configuración de detección con el nivel nuevo. La configuración puede ser un
    /// objeto o un arreglo por canal; se cambia "Level" donde exista. Devuelve null si no hay "Level".
    /// </summary>
    public static JsonNode? WithLevel(JsonNode? config, int level)
    {
        if (config is null) return null;
        var copy = config.DeepClone();
        var changed = false;

        void Apply(JsonNode? node)
        {
            if (node is JsonObject obj && obj.ContainsKey("Level"))
            {
                obj["Level"] = level;
                changed = true;
            }
        }

        if (copy is JsonArray array)
            foreach (var item in array) Apply(item);
        else
            Apply(copy);

        return changed ? copy : null;
    }
}
