using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CamGuard.Api.Common;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Notifications;
using CamGuard.Api.Common.Realtime;
using CamGuard.Api.Features.Devices;
using CamGuard.Api.Features.Monitoring;
using CamGuard.Api.Features.Upload;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Tuya;

/// <summary>
/// Escucha el Message Service de Tuya (Pulsar por WebSocket) y convierte los DPs de los dispositivos
/// Tuya con cámara (cámaras, timbres, cerraduras) en lo mismo que produce el sondeo TCP de las iCSee:
/// movimiento, timbre y alarmas publican "despertó" (graba)
/// y, al vencer <see cref="TuyaOptions.ActivationSeconds"/>, "dormida". Cada evento queda en BD y
/// se avisa al instante por ntfy; la batería se guarda en la cámara.
/// </summary>
public class TuyaEventService(
    IServiceScopeFactory scopeFactory,
    TuyaApiClient api,
    CameraStatePublisher publisher,
    INotifier notifier,
    IHubContext<EventsHub, IEventsClient> hub,
    IOptions<TuyaOptions> options,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<TuyaEventService> logger) : BackgroundService
{
    private static readonly TimeSpan CameraRefresh = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Una misma señal repetida en este lapso no se vuelve a avisar: reenvíos tras reconectar, o un
    /// timbre que reporta doorbell_active y doorbell_pic a la vez.
    /// </summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(15);

    private readonly TuyaActivationWindow _window = new(TimeSpan.FromSeconds(options.Value.ActivationSeconds));
    private readonly Dictionary<string, DateTimeOffset> _recentSignals = new();
    private readonly SemaphoreSlim _camerasLock = new(1, 1);
    private Dictionary<string, Camera> _cameras = new();

    /// <summary>Device ID de un sensor → cámaras que despierta (Camera.TuyaTriggerDeviceIds).</summary>
    private Dictionary<string, List<Camera>> _triggers = new();
    private DateTimeOffset _camerasLoadedAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
        {
            logger.LogInformation("Tuya sin configurar (TUYA_ACCESS_ID / TUYA_ACCESS_SECRET): no se escuchan dispositivos Tuya");
            return;
        }

        await Task.WhenAll(ListenLoopAsync(stoppingToken), TickLoopAsync(stoppingToken));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(() => attempt = 0, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Conexión con el Message Service de Tuya fallida: {Message}", ex.Message);
            }

            attempt++;
            var delay = RetryPolicy.Delay(attempt);
            try
            {
                await Task.Delay(delay > MaxReconnectDelay ? MaxReconnectDelay : delay, clock, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ListenAsync(Action onConnected, CancellationToken ct)
    {
        var o = options.Value;
        var url = $"{o.MessageEndpoint.TrimEnd('/')}/ws/v2/consumer/persistent/{o.AccessId}/out/{o.MessageTopic}/{o.AccessId}-sub"
            + "?ackTimeoutMillis=3000&subscriptionType=Failover";

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("username", o.AccessId);
        ws.Options.SetRequestHeader("password", TuyaMessageDecoder.MqPassword(o.AccessId, o.AccessSecret));
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await ws.ConnectAsync(new Uri(url), ct);
        onConnected();
        logger.LogInformation("Conectado al Message Service de Tuya ({Topic})", o.MessageTopic);

        var buffer = new byte[64 * 1024];
        while (ws.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                logger.LogWarning("Tuya cerró el Message Service: {Status} {Description}", ws.CloseStatus, ws.CloseStatusDescription);
                return;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(message.ToArray());
            }
            catch (JsonException)
            {
                continue;
            }
            if (node is null) continue;

            var envelope = TuyaMessageDecoder.Open(node, o.AccessSecret);
            LogEnvelope(envelope);
            if (envelope.Report is { } report)
            {
                try
                {
                    await HandleReportAsync(report, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex, "Error al procesar un reporte de Tuya");
                }
            }

            // Sin ack, Tuya reenvía el mensaje.
            if (TuyaMessageDecoder.MessageId(node) is { } messageId)
            {
                var ack = Encoding.UTF8.GetBytes(new JsonObject { ["messageId"] = messageId }.ToJsonString());
                await ws.SendAsync(ack, WebSocketMessageType.Text, true, ct);
            }
        }
    }

    /// <summary>
    /// Cada mensaje queda en el log con sus DPs: así se descubren los códigos de un dispositivo nuevo.
    /// Los valores largos (URLs de imágenes con firma) se recortan.
    /// </summary>
    private void LogEnvelope(TuyaEnvelope envelope)
    {
        if (envelope.Error is not null && envelope.Body is null)
        {
            logger.LogWarning("Mensaje de Tuya sin descifrar (protocolo {Protocol}, cifrado {Em}): {Error}",
                envelope.Protocol, envelope.Em ?? "sin em", envelope.Error);
            return;
        }

        // Los reportes se registran en HandleReportAsync, que sabe si el dispositivo interesa.
        if (envelope.Report is not null) return;

        // Otros protocolos (p. ej. 20: en línea/fuera de línea): solo el tipo de evento.
        logger.LogDebug("Mensaje de Tuya protocolo {Protocol}: {BizCode} {DevId}",
            envelope.Protocol, (string?)envelope.Body?["bizCode"], (string?)envelope.Body?["devId"]);
    }

    private static string? Shorten(string? value) => value is { Length: > 60 } ? value[..60] + "…" : value;

    private async Task HandleReportAsync(TuyaStatusReport report, CancellationToken ct)
    {
        var cameras = await GetCamerasAsync(ct);
        var ownCamera = cameras.GetValueOrDefault(report.DevId);
        var triggered = _triggers.GetValueOrDefault(report.DevId) ?? [];
        var codes = string.Join(", ", report.Status.Select(s => $"{s.Code}={Shorten(s.Value?.ToJsonString())}"));
        if (ownCamera is null && triggered.Count == 0)
        {
            logger.LogDebug("Reporte de Tuya de un dispositivo que no es cámara ni sensor de una {DevId}: {Codes}", report.DevId, codes);
            return;
        }
        logger.LogInformation("Reporte de Tuya {DevId}: {Codes}", report.DevId, codes);

        foreach (var status in report.Status)
        {
            if (TuyaEventRules.Interpret(status.Code, status.Value) is not { } signal) continue;

            if (ownCamera is not null)
            {
                if (signal.Kind == TuyaSignalKind.Battery)
                    await UpdateBatteryAsync(ownCamera.Id, signal.BatteryPercent!.Value, ct);
                else
                    await HandleSignalAsync(ownCamera, signal, status.At, notify: true, ct);
            }

            // Sensor externo: solo su movimiento despierta las cámaras asociadas. Sin aviso inmediato
            // (el sensor ya avisa por su lado); llega el aviso del clip.
            if (signal.Kind == TuyaSignalKind.Motion)
            {
                var sensorSignal = signal with { Detail = $"sensor {report.DevId}", Text = "Movimiento detectado por un sensor" };
                foreach (var camera in triggered)
                    await HandleSignalAsync(camera, sensorSignal, status.At, notify: false, ct);
            }
        }
    }

    private async Task HandleSignalAsync(Camera camera, TuyaSignal signal, DateTimeOffset? at, bool notify, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (IsDuplicate(camera.Id, signal, now)) return;

        // Mensajes acumulados mientras CamGuard estaba apagado: quedan en el historial, sin avisar ni grabar.
        if (IsStale(at, now))
        {
            logger.LogInformation("Tuya {Name}: {Text} (evento de {At:HH:mm:ss}, atrasado: no se avisa ni graba)",
                camera.Name, signal.Text, at!.Value.ToLocalTime());
            await SaveEventAsync(camera.Id, signal, at.Value, ct);
            return;
        }

        logger.LogInformation("Tuya {Name}: {Text}", camera.Name, signal.Text);
        await SaveEventAsync(camera.Id, signal, at ?? now, ct);
        if (notify) _ = NotifyAsync(camera, signal, at ?? now);

        if (signal.TriggersRecording && _window.Trigger(camera.Id, now))
            await publisher.PublishAsync(camera, CameraEventType.Awake, ct);
    }

    /// <summary>Sensor → cámaras que despierta. Un mismo sensor puede despertar varias cámaras.</summary>
    public static Dictionary<string, List<Camera>> BuildTriggerMap(IEnumerable<Camera> cameras)
    {
        var map = new Dictionary<string, List<Camera>>(StringComparer.Ordinal);
        foreach (var camera in cameras)
        foreach (var sensor in Camera.SplitDeviceIds(camera.TuyaTriggerDeviceIds))
        {
            if (!map.TryGetValue(sensor, out var list)) map[sensor] = list = [];
            list.Add(camera);
        }
        return map;
    }

    /// <summary>Un evento con más de este atraso llegó acumulado (CamGuard estuvo desconectado).</summary>
    public static readonly TimeSpan MaxEventAge = TimeSpan.FromMinutes(2);

    public static bool IsStale(DateTimeOffset? at, DateTimeOffset now) => at is { } t && now - t > MaxEventAge;

    private bool IsDuplicate(int cameraId, TuyaSignal signal, DateTimeOffset now)
    {
        var key = $"{cameraId}|{signal.Kind}|{signal.Detail}";
        lock (_recentSignals)
        {
            foreach (var old in _recentSignals.Where(s => now - s.Value > DuplicateWindow).Select(s => s.Key).ToList())
                _recentSignals.Remove(old);
            if (_recentSignals.ContainsKey(key)) return true;
            _recentSignals[key] = now;
            return false;
        }
    }

    private async Task SaveEventAsync(int cameraId, TuyaSignal signal, DateTimeOffset at, CancellationToken ct)
    {
        if (signal.EventType is not { } type) return;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CameraEvents.Add(new CameraEvent { CameraId = cameraId, Type = type, OccurredAt = at, Detail = signal.Detail });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Aviso inmediato, sin esperar el clip (el video de Tuya tarda en llegar).</summary>
    private async Task NotifyAsync(Camera camera, TuyaSignal signal, DateTimeOffset at)
    {
        try
        {
            await notifier.NotifyAsync(new Notification(
                Title: $"{signal.Title}: {camera.Name}",
                Message: $"{signal.Text} a las {at.ToLocalTime():HH:mm:ss}",
                ClickUrl: appOptions.Value.PublicBaseUrl,
                Tags: [signal.Kind switch
                {
                    TuyaSignalKind.Motion => "video_camera",
                    TuyaSignalKind.Doorbell => "bell",
                    TuyaSignalKind.Unlock => "unlock",
                    TuyaSignalKind.LockEvent => "camera_flash",
                    _ => "rotating_light"
                }]), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("No se pudo enviar el aviso de {Name}: {Message}", camera.Name, ex.Message);
        }
    }

    private async Task UpdateBatteryAsync(int cameraId, int percent, CancellationToken ct, string? model = null)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var camera = await db.Cameras.FindAsync([cameraId], ct);
        if (camera is null) return;

        camera.BatteryPercent = percent;
        camera.DeviceInfoAt = clock.GetUtcNow();
        if (string.IsNullOrWhiteSpace(camera.Model) && !string.IsNullOrWhiteSpace(model)) camera.Model = model;
        await db.SaveChangesAsync(ct);
        await hub.Clients.All.CameraInfoUpdated(DeviceInfoService.ToMessage(camera));
    }

    /// <summary>Cierra las activaciones vencidas y, cada tanto, lee la batería por la API.</summary>
    private async Task TickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        var nextStatusRefresh = clock.GetUtcNow();
        try
        {
            do
            {
                try
                {
                    var now = clock.GetUtcNow();
                    var cameras = await GetCamerasAsync(ct);
                    foreach (var id in _window.Expire(now))
                    {
                        if (cameras.Values.FirstOrDefault(c => c.Id == id) is { } camera)
                            await publisher.PublishAsync(camera, CameraEventType.Asleep, ct);
                    }

                    if (now >= nextStatusRefresh)
                    {
                        nextStatusRefresh = now.AddMinutes(options.Value.StatusRefreshMinutes);
                        await RefreshStatusAsync(cameras.Values, ct);
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning("Error en el ciclo de Tuya: {Message}", ex.Message);
                }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshStatusAsync(IEnumerable<Camera> cameras, CancellationToken ct)
    {
        foreach (var camera in cameras)
        {
            try
            {
                var model = string.IsNullOrWhiteSpace(camera.Model)
                    ? (await api.GetDeviceAsync(camera.TuyaDeviceId!, ct)).ProductName
                    : null;
                var status = await api.GetStatusAsync(camera.TuyaDeviceId!, ct);
                var battery = status
                    .Select(s => TuyaEventRules.Interpret(s.Code, s.Value))
                    .FirstOrDefault(s => s?.Kind == TuyaSignalKind.Battery);
                if (battery is not null)
                    await UpdateBatteryAsync(camera.Id, battery.BatteryPercent!.Value, ct, model);
            }
            catch (Exception ex) when (ex is TuyaApiException or HttpRequestException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("No se pudo leer el estado del dispositivo Tuya {Name}: {Message}", camera.Name, ex.Message);
            }
        }
    }

    /// <summary>Dispositivos Tuya habilitados por Device ID. Los borrados o deshabilitados cierran su activación.</summary>
    private async Task<Dictionary<string, Camera>> GetCamerasAsync(CancellationToken ct)
    {
        await _camerasLock.WaitAsync(ct);
        try
        {
            if (clock.GetUtcNow() - _camerasLoadedAt < CameraRefresh) return _cameras;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var loaded = await db.Cameras.AsNoTracking()
                .Where(c => c.Enabled && c.Kind == CameraKind.Tuya && c.TuyaDeviceId != null)
                .ToDictionaryAsync(c => c.TuyaDeviceId!, ct);

            foreach (var removed in _cameras.Values.Where(c => loaded.Values.All(l => l.Id != c.Id)))
            {
                if (!_window.Remove(removed.Id)) continue;
                try
                {
                    await publisher.PublishAsync(removed, CameraEventType.Asleep, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning("No se pudo registrar el cierre de {Name}: {Message}", removed.Name, ex.Message);
                }
            }

            _cameras = loaded;
            _triggers = BuildTriggerMap(loaded.Values);
            _camerasLoadedAt = clock.GetUtcNow();
            return _cameras;
        }
        finally
        {
            _camerasLock.Release();
        }
    }
}

public static class TuyaFeature
{
    public static IServiceCollection AddTuyaFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TuyaOptions>(configuration.GetSection(TuyaOptions.Section));
        services.AddHttpClient(TuyaApiClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<TuyaApiClient>();
        services.AddTransient<TuyaStreamPreparer>();
        services.AddHostedService<TuyaEventService>();
        return services;
    }
}
