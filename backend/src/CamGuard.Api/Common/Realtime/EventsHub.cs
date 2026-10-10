using CamGuard.Api.Common.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CamGuard.Api.Common.Realtime;

public record CameraStatusMessage(int CameraId, DateTimeOffset At);

public record RecordingStartedMessage(Guid RecordingId, int CameraId, DateTimeOffset StartedAt);

public record RecordingCompletedMessage(Guid RecordingId, int CameraId, DateTimeOffset StartedAt);

/// <summary>Último estado leído de la cámara por DVRIP (ver Features/Devices).</summary>
public record CameraInfoMessage(
    int CameraId,
    string? Model,
    int? BatteryPercent,
    long? StorageUsedBytes,
    long? StorageTotalBytes,
    MotionSensitivity MotionSensitivity,
    bool SensitivityPending,
    DateTimeOffset? DeviceInfoAt);

public interface IEventsClient
{
    Task CameraAwake(CameraStatusMessage message);
    Task CameraAsleep(CameraStatusMessage message);
    Task CameraInfoUpdated(CameraInfoMessage message);
    Task RecordingStarted(RecordingStartedMessage message);
    Task RecordingCompleted(RecordingCompletedMessage message);
}

/// <summary>Solo empuja eventos del servidor; el cliente no invoca métodos.</summary>
[Authorize]
public class EventsHub : Hub<IEventsClient>
{
    public const string Path = "/hubs/events";
}
