using CamGuard.Api.Features.Detection;

namespace CamGuard.Api.Features.Upload;

/// <summary>Prioridad de un clip en espera: se borran primero los de menor valor.</summary>
public enum EvictionTier
{
    /// <summary>La IA no vio personas ni animales.</summary>
    Nothing = 0,

    /// <summary>Sin analizar (modo Off o el análisis falló).</summary>
    Unanalyzed = 1,

    Animal = 2,
    Person = 3,

    /// <summary>Timbre, alarma o evento de cerradura Tuya: lo último que se borra.</summary>
    PriorityEvent = 4
}

public record EvictionCandidate(Guid Id, DateTimeOffset StartedAt, long Bytes, EvictionTier Tier);

/// <summary>
/// Sin internet los clips en <c>Uploading</c> se acumulan en el equipo. Al pasar el tope (o quedar poco disco)
/// se borran por prioridad y, dentro de la misma, los más antiguos primero.
/// </summary>
public static class LocalEvictionPolicy
{
    public static EvictionTier Tier(string? detection, bool hasPriorityEvent) => detection switch
    {
        _ when hasPriorityEvent => EvictionTier.PriorityEvent,
        CocoClasses.PersonLabel => EvictionTier.Person,
        CocoClasses.NothingLabel => EvictionTier.Nothing,
        _ when CocoClasses.IsAnimalLabel(detection) => EvictionTier.Animal,
        _ => EvictionTier.Unanalyzed
    };

    /// <summary>¿Hace falta borrar? Más que el tope usado o menos que el mínimo libre.</summary>
    public static bool IsOver(long usedBytes, long freeBytes, long maxBytes, long minFreeBytes) =>
        usedBytes > maxBytes || freeBytes < minFreeBytes;

    /// <summary>
    /// Los clips a borrar hasta que <c>used ≤ max</c> y <c>free ≥ minFree</c>. Si ni borrándolos todos
    /// alcanza, los devuelve todos (el aviso de disco de Health ya está abierto).
    /// </summary>
    public static IReadOnlyList<EvictionCandidate> Select(
        IEnumerable<EvictionCandidate> candidates, long usedBytes, long freeBytes, long maxBytes, long minFreeBytes)
    {
        var selected = new List<EvictionCandidate>();
        foreach (var c in candidates.OrderBy(c => c.Tier).ThenBy(c => c.StartedAt))
        {
            if (!IsOver(usedBytes, freeBytes, maxBytes, minFreeBytes)) break;
            selected.Add(c);
            usedBytes -= c.Bytes;
            freeBytes += c.Bytes;
        }
        return selected;
    }
}
