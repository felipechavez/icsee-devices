namespace CamGuard.Api.Common;

/// <summary>Backoff de reintentos compartido por la cola de subida y la bandeja de avisos.</summary>
public static class RetryPolicy
{
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(10);

    /// <summary>Backoff exponencial: 5 s, 10 s, 20 s… hasta 10 min. Nunca se rinde.</summary>
    public static TimeSpan Delay(int attempts)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 20);
        var delay = BaseDelay * Math.Pow(2, exponent);
        return delay > MaxDelay ? MaxDelay : delay;
    }
}
