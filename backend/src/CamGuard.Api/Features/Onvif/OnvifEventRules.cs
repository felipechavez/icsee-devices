namespace CamGuard.Api.Features.Onvif;

public enum OnvifSignalKind
{
    Motion,
    Tamper
}

/// <param name="Active">true = empezó (hay movimiento / la taparon); false = terminó.</param>
/// <param name="Initial">Estado que la cámara informa al suscribirse ("Initialized"), no un cambio nuevo.</param>
public record OnvifSignal(OnvifSignalKind Kind, bool Active, bool Initial);

/// <summary>
/// Traduce los eventos ONVIF a señales de CamGuard. Los fabricantes usan tópicos y nombres distintos:
/// Dahua/Imou mandan VideoSource/MotionAlarm con State; el perfil estándar usa RuleEngine/CellMotionDetector/Motion
/// con IsMotion. Para una cámara con otros nombres, revisa sus tópicos con poc/imou/onvif-events.ps1 y agrégalos aquí.
/// </summary>
public static class OnvifEventRules
{
    private static readonly string[] MotionTopics =
    [
        "VideoSource/MotionAlarm",
        "RuleEngine/CellMotionDetector/Motion",
        "RuleEngine/MotionDetector/Motion",
        "RuleEngine/MotionRegionDetector/Motion"
    ];

    private static readonly string[] MotionItems = ["State", "IsMotion"];

    private static readonly string[] TamperTopics =
    [
        "RuleEngine/TamperDetector/Tamper",
        "VideoSource/ImageTooBlurry",
        "VideoSource/ImageTooDark"
    ];

    private static readonly string[] TamperItems = ["IsTamper", "State"];

    public static OnvifSignal? Interpret(OnvifNotification n)
    {
        if (Matches(n.Topic, MotionTopics) && Flag(n.Data, MotionItems) is { } motion)
            return new OnvifSignal(OnvifSignalKind.Motion, motion, IsInitial(n));

        if (Matches(n.Topic, TamperTopics) && Flag(n.Data, TamperItems) is { } tamper)
            return new OnvifSignal(OnvifSignalKind.Tamper, tamper, IsInitial(n));

        return null;
    }

    private static bool IsInitial(OnvifNotification n) => string.Equals(n.Operation, "Initialized", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string topic, string[] candidates) =>
        candidates.Any(c => topic.Equals(c, StringComparison.OrdinalIgnoreCase)
                            || topic.EndsWith("/" + c, StringComparison.OrdinalIgnoreCase));

    private static bool? Flag(IReadOnlyDictionary<string, string> data, string[] names)
    {
        foreach (var name in names)
        {
            if (!data.TryGetValue(name, out var value)) continue;
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1") return true;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0") return false;
        }
        return null;
    }
}
