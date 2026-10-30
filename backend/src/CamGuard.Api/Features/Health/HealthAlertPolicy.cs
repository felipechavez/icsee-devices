using CamGuard.Api.Common.Data;

namespace CamGuard.Api.Features.Health;

/// <param name="ToOpen">Problemas nuevos: se crea su <see cref="HealthAlert"/>.</param>
/// <param name="ToNotify">Nuevos, pendientes de un aviso que falló o que llevan <c>RepeatHours</c> sin aviso.</param>
/// <param name="ToResolve">Abiertos que ya no aparecen.</param>
public record AlertDecision(IReadOnlyList<HealthIssue> ToOpen, IReadOnlyList<HealthIssue> ToNotify, IReadOnlyList<HealthAlert> ToResolve);

/// <summary>Qué avisar por ntfy según los problemas actuales y los avisos abiertos.</summary>
public static class HealthAlertPolicy
{
    public static AlertDecision Decide(
        IReadOnlyList<HealthIssue> issues,
        IReadOnlyDictionary<string, HealthAlert> open,
        DateTimeOffset now,
        TimeSpan repeatAfter)
    {
        var current = issues.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        var toOpen = issues.Where(i => !open.ContainsKey(i.Key)).ToList();
        var toNotify = issues
            .Where(i => !open.TryGetValue(i.Key, out var a) || a.LastNotifiedAt is not { } last || now - last >= repeatAfter)
            .ToList();
        var toResolve = open.Values.Where(a => !current.Contains(a.Key)).ToList();
        return new AlertDecision(toOpen, toNotify, toResolve);
    }

    /// <summary>Un problema que nunca se llegó a avisar se cierra en silencio.</summary>
    public static bool NotifyResolution(HealthAlert alert) => alert.LastNotifiedAt is not null;
}
