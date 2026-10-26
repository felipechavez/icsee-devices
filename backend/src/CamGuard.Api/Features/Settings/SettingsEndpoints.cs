using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;

namespace CamGuard.Api.Features.Settings;

public record SettingsDto(int MaxClipSeconds, int CooldownSeconds, int RetentionDays, double DetectionMinConfidence = 0.5);

public static class GetSettings
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/settings", async (AppDbContext db, CancellationToken ct) =>
                {
                    var s = await db.GetSettingsAsync(ct);
                    return new SettingsDto(s.MaxClipSeconds, s.CooldownSeconds, s.RetentionDays, s.DetectionMinConfidence);
                })
                .WithTags("Settings");
        }
    }
}

public static class UpdateSettings
{
    public const int MinClipSeconds = 5;
    public const int MaxClipSeconds = 600;
    public const int MaxCooldownSeconds = 600;
    public const int MaxRetentionDays = 365;
    public const double MinDetectionConfidence = 0.2;
    public const double MaxDetectionConfidence = 0.95;

    public static Dictionary<string, string[]> Validate(SettingsDto r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.MaxClipSeconds is < MinClipSeconds or > MaxClipSeconds)
            errors[nameof(r.MaxClipSeconds)] = [$"Debe estar entre {MinClipSeconds} y {MaxClipSeconds} segundos."];
        if (r.CooldownSeconds is < 0 or > MaxCooldownSeconds)
            errors[nameof(r.CooldownSeconds)] = [$"Debe estar entre 0 y {MaxCooldownSeconds} segundos."];
        if (r.RetentionDays is < 1 or > MaxRetentionDays)
            errors[nameof(r.RetentionDays)] = [$"Debe estar entre 1 y {MaxRetentionDays} días."];
        if (r.DetectionMinConfidence is < MinDetectionConfidence or > MaxDetectionConfidence || double.IsNaN(r.DetectionMinConfidence))
            errors[nameof(r.DetectionMinConfidence)] = ["La confianza mínima debe estar entre 20 % y 95 %."];
        return errors;
    }

    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPut("/settings", async (SettingsDto request, AppDbContext db, CancellationToken ct) =>
                {
                    var errors = Validate(request);
                    if (errors.Count > 0)
                        return Results.ValidationProblem(errors);

                    var s = await db.GetSettingsAsync(ct);
                    s.MaxClipSeconds = request.MaxClipSeconds;
                    s.CooldownSeconds = request.CooldownSeconds;
                    s.RetentionDays = request.RetentionDays;
                    s.DetectionMinConfidence = request.DetectionMinConfidence;
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(request);
                })
                .WithTags("Settings");
        }
    }
}
