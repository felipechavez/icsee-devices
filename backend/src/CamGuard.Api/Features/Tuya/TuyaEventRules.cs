using System.Text.Json;
using System.Text.Json.Nodes;
using CamGuard.Api.Common.Data;

namespace CamGuard.Api.Features.Tuya;

public enum TuyaSignalKind
{
    Motion,
    Doorbell,
    Alarm,
    Hijack,
    Unlock,
    Battery,

    /// <summary>Evento con foto de una cerradura cuyo tipo aún no se identificó: graba por si acaso.</summary>
    LockEvent
}

/// <param name="Detail">Se guarda en <see cref="CameraEvent.Detail"/>, p. ej. "wrong_password" o "fingerprint #1".</param>
/// <param name="Text">Texto del aviso inmediato.</param>
public record TuyaSignal(TuyaSignalKind Kind, string? Detail = null, string Text = "", int? BatteryPercent = null)
{
    /// <summary>Movimiento, timbre y alarmas graban; aperturas y batería solo se registran.</summary>
    public bool TriggersRecording => Kind is TuyaSignalKind.Motion or TuyaSignalKind.Doorbell or TuyaSignalKind.Hijack
            or TuyaSignalKind.LockEvent
        || (Kind == TuyaSignalKind.Alarm && Detail is not ("low_battery" or "too_hot"));

    public CameraEventType? EventType => Kind switch
    {
        TuyaSignalKind.Motion => CameraEventType.Motion,
        TuyaSignalKind.Doorbell => CameraEventType.Doorbell,
        TuyaSignalKind.Alarm or TuyaSignalKind.Hijack => CameraEventType.Alarm,
        TuyaSignalKind.Unlock => CameraEventType.Unlock,
        TuyaSignalKind.LockEvent => CameraEventType.LockEvent,
        _ => null
    };

    public string Title => Kind switch
    {
        TuyaSignalKind.Motion => "Movimiento",
        TuyaSignalKind.Doorbell => "Timbre",
        TuyaSignalKind.Alarm or TuyaSignalKind.Hijack => "Alarma",
        TuyaSignalKind.Unlock => "Apertura",
        TuyaSignalKind.LockEvent => "Cerradura",
        _ => "Tuya"
    };
}

/// <summary>
/// Traduce los DPs de un dispositivo Tuya con cámara a señales de CamGuard. Cubre los códigos
/// estándar de Tuya para cámaras y timbres (categoría sp: movement_detect_pic, doorbell_active,
/// wireless_electricity…) y para cerraduras con video (videolock, vistos en la XOOMI C20:
/// doorbell, alarm_lock, hijack, unlock_*, residual_electricity). Un firmware con otros códigos
/// se descubre con poc/tuya/probe-device.ps1 y se agrega aquí.
/// </summary>
public static class TuyaEventRules
{
    private static readonly Dictionary<string, string> UnlockMethods = new()
    {
        ["unlock_fingerprint"] = "huella",
        ["unlock_password"] = "clave",
        ["unlock_card"] = "tarjeta",
        ["unlock_face"] = "rostro",
        ["unlock_hand"] = "palma",
        ["unlock_key"] = "llave",
        ["unlock_phone_remote"] = "la app",
        ["unlock_app"] = "la app",
        ["unlock_temporary"] = "clave temporal",
        ["unlock_dynamic"] = "clave dinámica"
    };

    private static readonly Dictionary<string, string> AlarmTexts = new()
    {
        ["wrong_finger"] = "huella no reconocida",
        ["wrong_password"] = "clave errónea",
        ["wrong_card"] = "tarjeta no válida",
        ["wrong_face"] = "rostro no reconocido",
        ["tongue_bad"] = "cerrojo bloqueado o forzado",
        ["tongue_not_out"] = "cerrojo sin extender",
        ["unclosed_time"] = "puerta abierta mucho tiempo",
        ["key_in"] = "llave insertada",
        ["too_hot"] = "temperatura alta",
        ["low_battery"] = "batería baja"
    };

    /// <summary>Códigos de batería (%) según el tipo de dispositivo.</summary>
    private static readonly HashSet<string> BatteryCodes = ["residual_electricity", "wireless_electricity", "battery_percentage"];

    /// <summary>
    /// <c>ext.type</c> de los eventos con foto/video de una cerradura (<c>initiative_message</c>,
    /// <c>cmd = door_lock_video</c>). Vistos en la XOOMI C20: 18 = timbre (llega junto con el DP doorbell);
    /// 16 aún sin identificar. Un tipo desconocido graba y avisa como evento genérico.
    /// </summary>
    private static readonly Dictionary<int, (TuyaSignalKind Kind, string Text)> LockMediaTypes = new()
    {
        [18] = (TuyaSignalKind.Doorbell, "Tocaron el timbre")
    };

    /// <summary>Foto de una apertura (XOOMI C20: 8 = huella, llega junto con unlock_fingerprint).</summary>
    private static readonly HashSet<int> UnlockMediaTypes = [8];

    public static TuyaSignal? Interpret(string code, JsonNode? value)
    {
        switch (code)
        {
            // Cerraduras con video: el evento llega como JSON en base64 con foto y clip propios.
            case "initiative_message":
                return InterpretLockMedia(value);

            // Cámaras: llega con la imagen del evento (URL o base64); cualquier valor es movimiento.
            case "movement_detect_pic":
                return HasValue(value) ? new TuyaSignal(TuyaSignalKind.Motion, Text: "Movimiento detectado") : null;

            // Sensores PIR (categoría pir): "pir" o true al detectar, "none" o false al terminar.
            // Algunos firmwares reportan el código en mayúsculas ("PIR").
            case "pir":
            case "PIR":
                return AsBool(value) == true || AsString(value) == "pir"
                    ? new TuyaSignal(TuyaSignalKind.Motion, Text: "Movimiento detectado")
                    : null;

            case "doorbell":
            case "doorbell_active":
                return AsBool(value) == true || (AsBool(value) is null && HasValue(value))
                    ? new TuyaSignal(TuyaSignalKind.Doorbell, Text: "Tocaron el timbre")
                    : null;

            case "doorbell_pic":
                return HasValue(value) ? new TuyaSignal(TuyaSignalKind.Doorbell, Text: "Tocaron el timbre") : null;

            case "hijack":
                return AsBool(value) == true ? new TuyaSignal(TuyaSignalKind.Hijack, "hijack", "Apertura bajo coacción") : null;

            case "alarm_lock":
                if (AsString(value) is not { Length: > 0 } alarm) return null;
                return new TuyaSignal(TuyaSignalKind.Alarm, alarm, $"Alarma: {AlarmTexts.GetValueOrDefault(alarm, alarm)}");
        }

        if (BatteryCodes.Contains(code))
        {
            return AsInt(value) is { } pct and >= 0 and <= 100
                ? new TuyaSignal(TuyaSignalKind.Battery, BatteryPercent: pct)
                : null;
        }

        if (UnlockMethods.TryGetValue(code, out var method))
        {
            var user = AsInt(value);
            var detail = $"{code["unlock_".Length..]}{(user is null ? "" : $" #{user}")}";
            var text = $"Abrieron con {method}{(user is null ? "" : $" (usuario {user})")}";
            return new TuyaSignal(TuyaSignalKind.Unlock, detail, text);
        }

        return null;
    }

    /// <summary>
    /// <c>{"v":"5.0","cmd":"door_lock_video","type":"media","alarm":true,"time":…,"files":[…],"ext":{"type":8,…}}</c>.
    /// Otros comandos de <c>initiative_message</c> (p. ej. pedidos de apertura remota) no son eventos de cámara.
    /// </summary>
    private static TuyaSignal? InterpretLockMedia(JsonNode? value)
    {
        if (AsString(value) is not { Length: > 0 } encoded) return null;

        JsonNode? message;
        try
        {
            message = JsonNode.Parse(Convert.FromBase64String(encoded));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }

        if ((string?)message?["cmd"] != "door_lock_video") return null;

        var type = AsInt(message["ext"]?["type"]);
        // Foto de una apertura: el DP unlock_* ya la avisa, y las aperturas no graban.
        if (type is { } u && UnlockMediaTypes.Contains(u)) return null;

        // Sin Detail: así el aviso se deduplica con el DP equivalente (p. ej. doorbell) del mismo evento.
        if (type is { } t && LockMediaTypes.TryGetValue(t, out var known))
            return new TuyaSignal(known.Kind, Text: known.Text);

        return new TuyaSignal(TuyaSignalKind.LockEvent, $"media #{type?.ToString() ?? "?"}",
            $"Evento de la cerradura con foto (tipo {type?.ToString() ?? "desconocido"})");
    }

    private static bool HasValue(JsonNode? node) => node switch
    {
        null => false,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>().Length > 0,
        JsonValue v when v.GetValueKind() == JsonValueKind.False => false,
        _ => true
    };

    private static bool? AsBool(JsonNode? node) => node is JsonValue v
        ? v.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetValue<string>(), out var b) ? b : null,
            _ => null
        }
        : null;

    private static int? AsInt(JsonNode? node) => node is JsonValue v
        ? v.GetValueKind() switch
        {
            JsonValueKind.Number => v.TryGetValue<int>(out var i) ? i : null,
            JsonValueKind.String => int.TryParse(v.GetValue<string>(), out var i) ? i : null,
            _ => null
        }
        : null;

    private static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
}
