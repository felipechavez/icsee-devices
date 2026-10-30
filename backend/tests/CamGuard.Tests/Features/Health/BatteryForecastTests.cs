using CamGuard.Api.Features.Health;

namespace CamGuard.Tests.Features.Health;

public class BatteryForecastTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static BatteryPoint At(double days, int percent) => new(T0.AddDays(days), percent);

    [Test]
    public async Task DaysLeft_LinearDischarge_ProjectsToZero()
    {
        // 10 puntos por día: de 80 % a 60 % en 2 días; quedan 6 días desde el día 2.
        var readings = new[] { At(0, 80), At(1, 70), At(2, 60) };

        var days = BatteryForecast.DaysLeft(readings, T0.AddDays(2));

        await Assert.That(days).IsNotNull();
        await Assert.That(days!.Value).IsEqualTo(6).Within(0.01);
    }

    [Test]
    public async Task DaysLeft_LaterNow_CountsFromNow()
    {
        var readings = new[] { At(0, 80), At(1, 70), At(2, 60) };

        var days = BatteryForecast.DaysLeft(readings, T0.AddDays(5));

        await Assert.That(days!.Value).IsEqualTo(3).Within(0.01);
    }

    [Test]
    public async Task DaysLeft_ChargeMidSeries_UsesOnlyLastDischarge()
    {
        // Antes de la carga bajaba 20 puntos por día; después, ~5 (con la serie completa la pendiente daría otra cosa).
        var readings = new[] { At(0, 60), At(1, 40), At(2, 20), At(2.1, 100), At(3, 95), At(4, 90) };

        var days = BatteryForecast.DaysLeft(readings, T0.AddDays(4));

        await Assert.That(BatteryForecast.DischargeSegment(readings).Count).IsEqualTo(3);
        await Assert.That(days!.Value).IsEqualTo(17.1).Within(0.1);
    }

    [Test]
    public async Task DischargeSegment_SmallNoise_DoesNotCut()
    {
        var readings = new[] { At(0, 80), At(1, 75), At(2, 77), At(3, 70) };

        await Assert.That(BatteryForecast.DischargeSegment(readings).Count).IsEqualTo(4);
    }

    [Test]
    public async Task DaysLeft_ShortSpan_Null()
    {
        var readings = new[] { At(0, 80), At(0.3, 78), At(0.6, 76) };

        await Assert.That(BatteryForecast.DaysLeft(readings, T0.AddDays(1))).IsNull();
    }

    [Test]
    public async Task DaysLeft_TooFewReadings_Null()
    {
        var readings = new[] { At(0, 80), At(3, 60) };

        await Assert.That(BatteryForecast.DaysLeft(readings, T0.AddDays(3))).IsNull();
    }

    [Test]
    [Arguments(80, 80, 80)]
    [Arguments(60, 62, 64)]
    public async Task DaysLeft_FlatOrRising_Null(int a, int b, int c)
    {
        var readings = new[] { At(0, a), At(1, b), At(2, c) };

        await Assert.That(BatteryForecast.DaysLeft(readings, T0.AddDays(2))).IsNull();
    }

    [Test]
    public async Task DaysLeft_PastProjectedEnd_Zero()
    {
        var readings = new[] { At(0, 30), At(1, 20), At(2, 10) };

        await Assert.That(BatteryForecast.DaysLeft(readings, T0.AddDays(10))).IsEqualTo(0);
    }
}

public class BatteryHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ShouldRecord_NoHistory_True()
    {
        await Assert.That(BatteryHistory.ShouldRecord(null, 80, T0)).IsTrue();
    }

    [Test]
    public async Task ShouldRecord_PercentChanged_True()
    {
        await Assert.That(BatteryHistory.ShouldRecord(new BatteryPoint(T0, 80), 79, T0.AddMinutes(5))).IsTrue();
    }

    [Test]
    public async Task ShouldRecord_SamePercentBeforeInterval_False()
    {
        await Assert.That(BatteryHistory.ShouldRecord(new BatteryPoint(T0, 80), 80, T0.AddHours(5))).IsFalse();
    }

    [Test]
    public async Task ShouldRecord_SamePercentAfterInterval_True()
    {
        await Assert.That(BatteryHistory.ShouldRecord(new BatteryPoint(T0, 80), 80, T0.AddHours(6))).IsTrue();
    }

    [Test]
    public async Task ShouldRecord_SameReading_False()
    {
        // La cámara no volvió a despertar: DeviceInfoAt no avanzó.
        await Assert.That(BatteryHistory.ShouldRecord(new BatteryPoint(T0, 80), 75, T0)).IsFalse();
    }
}
