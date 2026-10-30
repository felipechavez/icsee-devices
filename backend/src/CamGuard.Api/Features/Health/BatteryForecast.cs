namespace CamGuard.Api.Features.Health;

public record BatteryPoint(DateTimeOffset At, int Percent);

public static class BatteryHistory
{
    /// <summary>Aunque el porcentaje no cambie, se guarda un punto cada tanto para que el gráfico no quede vacío.</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromHours(6);

    /// <summary><paramref name="at"/> es la hora de la lectura (<c>Camera.DeviceInfoAt</c>), no la de la revisión.</summary>
    public static bool ShouldRecord(BatteryPoint? last, int percent, DateTimeOffset at) =>
        last is null || (at > last.At && (percent != last.Percent || at - last.At >= KeepAliveInterval));
}

/// <summary>Días que le quedan a una batería, por regresión lineal sobre la descarga desde la última carga.</summary>
public static class BatteryForecast
{
    /// <summary>Una subida de al menos estos puntos marca una carga.</summary>
    public const int ChargeJump = 10;

    public const int MinReadings = 3;
    public static readonly TimeSpan MinSpan = TimeSpan.FromHours(24);

    /// <summary>Null si no hay datos suficientes o la batería no está bajando.</summary>
    /// <param name="readings">En orden cronológico.</param>
    public static double? DaysLeft(IReadOnlyList<BatteryPoint> readings, DateTimeOffset now)
    {
        var segment = DischargeSegment(readings);
        if (segment.Count < MinReadings || segment[^1].At - segment[0].At < MinSpan) return null;

        var t0 = segment[0].At;
        var xs = segment.Select(p => (p.At - t0).TotalDays).ToArray();
        var ys = segment.Select(p => (double)p.Percent).ToArray();
        var (mx, my) = (xs.Average(), ys.Average());
        var sxx = xs.Sum(x => (x - mx) * (x - mx));
        var slope = xs.Zip(ys).Sum(p => (p.First - mx) * (p.Second - my)) / sxx;
        if (slope >= 0) return null;

        var percentNow = my + slope * ((now - t0).TotalDays - mx);
        return Math.Max(0, percentNow / -slope);
    }

    /// <summary>Las lecturas posteriores a la última carga (las que siguen al último salto hacia arriba).</summary>
    public static IReadOnlyList<BatteryPoint> DischargeSegment(IReadOnlyList<BatteryPoint> readings)
    {
        var start = 0;
        int? maxLater = null;
        for (var i = readings.Count - 1; i >= 0; i--)
        {
            if (readings[i].Percent <= maxLater - ChargeJump)
            {
                start = i + 1;
                break;
            }
            maxLater = Math.Max(maxLater ?? int.MinValue, readings[i].Percent);
        }
        return readings.Skip(start).ToList();
    }
}
