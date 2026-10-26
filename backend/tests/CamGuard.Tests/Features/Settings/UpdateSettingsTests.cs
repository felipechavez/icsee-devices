using CamGuard.Api.Features.Settings;

namespace CamGuard.Tests.Features.Settings;

public class UpdateSettingsTests
{
    [Test]
    public async Task Validate_DefaultValues_NoErrors()
    {
        var errors = UpdateSettings.Validate(new SettingsDto(60, 10, 30));

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    [Arguments(4, 10, 30, nameof(SettingsDto.MaxClipSeconds))]
    [Arguments(601, 10, 30, nameof(SettingsDto.MaxClipSeconds))]
    [Arguments(60, -1, 30, nameof(SettingsDto.CooldownSeconds))]
    [Arguments(60, 601, 30, nameof(SettingsDto.CooldownSeconds))]
    [Arguments(60, 10, 0, nameof(SettingsDto.RetentionDays))]
    [Arguments(60, 10, 366, nameof(SettingsDto.RetentionDays))]
    public async Task Validate_OutOfRange_ReportsField(int maxClip, int cooldown, int retention, string field)
    {
        var errors = UpdateSettings.Validate(new SettingsDto(maxClip, cooldown, retention));

        await Assert.That(errors.Keys).IsEquivalentTo([field]);
    }

    [Test]
    [Arguments(0.19)]
    [Arguments(0.96)]
    [Arguments(double.NaN)]
    public async Task Validate_DetectionConfidenceOutOfRange_Rejected(double confidence)
    {
        var errors = UpdateSettings.Validate(new SettingsDto(60, 10, 30, confidence));

        await Assert.That(errors.Keys).IsEquivalentTo([nameof(SettingsDto.DetectionMinConfidence)]);
    }

    [Test]
    [Arguments(0.2)]
    [Arguments(0.95)]
    public async Task Validate_DetectionConfidenceBoundaries_Accepted(double confidence)
    {
        await Assert.That(UpdateSettings.Validate(new SettingsDto(60, 10, 30, confidence))).IsEmpty();
    }

    [Test]
    [Arguments(5, 0, 1)]
    [Arguments(600, 600, 365)]
    public async Task Validate_BoundaryValues_Accepted(int maxClip, int cooldown, int retention)
    {
        await Assert.That(UpdateSettings.Validate(new SettingsDto(maxClip, cooldown, retention))).IsEmpty();
    }
}
