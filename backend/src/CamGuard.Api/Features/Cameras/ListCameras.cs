using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Common.Storage;
using CamGuard.Api.Features.Capture;
using CamGuard.Api.Features.Monitoring;
using CamGuard.Api.Features.Onvif;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Cameras;

public static class ListCameras
{
    /// <summary>La contraseña nunca sale de la API: solo se informa si está configurada.</summary>
    public record CameraResponse(
        int Id,
        string Name,
        string Ip,
        int DvripPort,
        string Go2rtcStream,
        bool Enabled,
        string Username,
        bool HasPassword,
        bool IsAwake,
        DateTimeOffset? LastChangedAt,
        string? Model = null,
        MotionSensitivity MotionSensitivity = MotionSensitivity.Medium,
        bool SensitivityPending = false,
        int? BatteryPercent = null,
        long? StorageUsedBytes = null,
        long? StorageTotalBytes = null,
        DateTimeOffset? DeviceInfoAt = null,
        bool IsRecording = false,
        DateTimeOffset? LastRecordingAt = null,
        string? LastThumbnailUrl = null,
        int? ClipsToday = null,
        CameraKind Kind = CameraKind.Icsee,
        string? TuyaDeviceId = null,
        int Rotation = 0,
        string? TuyaTriggerDeviceIds = null,
        DetectionMode DetectionMode = DetectionMode.Tag,
        bool DetectPeople = true,
        bool DetectAnimals = true,
        int OnvifPort = 80,
        bool? Connected = null);

    /// <summary>Último clip y miniatura de cada cámara, más los clips del día pedido.</summary>
    public record RecordingStats(DateTimeOffset? LastRecordingAt, string? LastThumbnailUrl, int? ClipsToday)
    {
        public static readonly RecordingStats None = new(null, null, null);
    }

    /// <param name="onvif">Conexión de eventos de una ONVIF (Features/Onvif); null = aún no se intentó o no es ONVIF.</param>
    public static CameraResponse ToResponse(Camera c, CameraState state, bool isRecording = false, RecordingStats? stats = null,
        OnvifConnection? onvif = null)
    {
        stats ??= RecordingStats.None;
        return new(c.Id, c.Name, c.Ip, c.DvripPort, c.Go2rtcStream, c.Enabled, c.Username,
            !string.IsNullOrEmpty(c.PasswordProtected), state.IsAwake, state.LastChangedAt,
            c.Model, c.MotionSensitivity, c.SensitivityPending, c.BatteryPercent, c.StorageUsedBytes, c.StorageTotalBytes,
            c.DeviceInfoAt, isRecording, stats.LastRecordingAt, stats.LastThumbnailUrl, stats.ClipsToday,
            c.Kind, c.TuyaDeviceId, c.Rotation, c.TuyaTriggerDeviceIds, c.DetectionMode, c.DetectPeople, c.DetectAnimals,
            c.OnvifPort, c.Kind == CameraKind.Onvif ? onvif?.Connected : null);
    }

    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            // todayFrom/todayTo: el día local del navegador, para contar "clips hoy" en su zona horaria.
            app.MapGet("/cameras", async (
                    DateTimeOffset? todayFrom,
                    DateTimeOffset? todayTo,
                    AppDbContext db,
                    CameraStateStore states,
                    ActivationGate gate,
                    OnvifConnectionState onvif,
                    IVideoStorage storage,
                    IOptions<StorageOptions> storageOptions,
                    CancellationToken ct) =>
                {
                    var cameras = await db.Cameras.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);

                    // Los descartados por la IA no cuentan: no hay clip que ver.
                    var recordings = db.Recordings.AsNoTracking().Where(r => r.Status != RecordingStatus.Discarded);
                    var last = await recordings
                        .GroupBy(r => r.CameraId)
                        .Select(g => new
                        {
                            CameraId = g.Key,
                            LastAt = g.Max(r => r.StartedAt),
                            Thumb = g.Where(r => r.Status == RecordingStatus.Uploaded && r.ThumbnailKey != null)
                                .OrderByDescending(r => r.StartedAt)
                                .Select(r => r.ThumbnailKey)
                                .FirstOrDefault()
                        })
                        .ToDictionaryAsync(x => x.CameraId, ct);

                    Dictionary<int, int>? today = null;
                    if (todayFrom is not null && todayTo is not null)
                    {
                        // Postgres (timestamptz) solo acepta DateTimeOffset en UTC.
                        var (fromUtc, toUtc) = (todayFrom.Value.ToUniversalTime(), todayTo.Value.ToUniversalTime());
                        today = await recordings
                            .Where(r => r.StartedAt >= fromUtc && r.StartedAt < toUtc)
                            .GroupBy(r => r.CameraId)
                            .Select(g => new { CameraId = g.Key, Count = g.Count() })
                            .ToDictionaryAsync(x => x.CameraId, x => x.Count, ct);
                    }

                    var ttl = TimeSpan.FromMinutes(storageOptions.Value.PresignedUrlMinutes);
                    return cameras.Select(c =>
                    {
                        var l = last.GetValueOrDefault(c.Id);
                        var stats = new RecordingStats(
                            l?.LastAt,
                            l?.Thumb is { } key ? storage.GetPresignedUrl(key, ttl) : null,
                            today is null ? null : today.GetValueOrDefault(c.Id));
                        return ToResponse(c, states.Get(c.Id), gate.IsRecording(c.Id), stats, onvif.Get(c.Id));
                    });
                })
                .WithTags("Cameras");
        }
    }
}
