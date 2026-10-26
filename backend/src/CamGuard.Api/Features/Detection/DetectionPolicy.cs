using CamGuard.Api.Common.Data;

namespace CamGuard.Api.Features.Detection;

/// <param name="Label">"persona", el animal ("perro"…) o "nada".</param>
/// <param name="Score">Confianza de lo hallado; con "nada", la mayor de las clases activas (bajo el umbral).</param>
public record DetectionVerdict(string Label, double Score, bool Found);

public enum DetectionDecision
{
    Keep,
    Discard
}

public static class DetectionPolicy
{
    /// <summary>
    /// El evento llega antes que el clip: lo despierta, y Tuya tarda ~30–40 s en dar el primer cuadro.
    /// Se buscan eventos prioritarios desde este margen antes del inicio de la grabación.
    /// </summary>
    public static readonly TimeSpan PriorityEventLookback = TimeSpan.FromMinutes(2);

    /// <summary>Si aparecen persona y animal, gana la persona; entre animales, el de mayor confianza.</summary>
    public static DetectionVerdict Evaluate(IEnumerable<ObjectDetection> detections, bool people, bool animals, double minConfidence)
    {
        var relevant = detections
            .Where(d => (people && d.ClassId == CocoClasses.Person) || (animals && CocoClasses.IsAnimal(d.ClassId)))
            .ToList();

        var person = relevant.Where(d => d.ClassId == CocoClasses.Person).MaxBy(d => d.Score);
        if (person is not null && person.Score >= minConfidence)
            return new DetectionVerdict(CocoClasses.PersonLabel, person.Score, true);

        var animal = relevant.Where(d => CocoClasses.IsAnimal(d.ClassId)).MaxBy(d => d.Score);
        if (animal is not null && animal.Score >= minConfidence)
            return new DetectionVerdict(CocoClasses.Name(animal.ClassId)!, animal.Score, true);

        return new DetectionVerdict(CocoClasses.NothingLabel, relevant.Count == 0 ? 0 : relevant.Max(d => d.Score), false);
    }

    /// <param name="verdict">Null si no se analizó o el análisis falló: entonces se conserva (fail-open).</param>
    /// <param name="hasPriorityEvent">
    /// Hubo un evento Tuya Doorbell, Alarm o LockEvent durante la grabación: esos clips nunca se descartan.
    /// </param>
    public static DetectionDecision Decide(DetectionMode mode, DetectionVerdict? verdict, bool hasPriorityEvent) =>
        mode == DetectionMode.Filter && verdict is { Found: false } && !hasPriorityEvent
            ? DetectionDecision.Discard
            : DetectionDecision.Keep;
}
