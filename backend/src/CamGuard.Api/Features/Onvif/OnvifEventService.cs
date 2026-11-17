using CamGuard.Api.Common;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Notifications;
using CamGuard.Api.Common.Realtime;
using CamGuard.Api.Common.Security;
using CamGuard.Api.Features.Devices;
using CamGuard.Api.Features.Monitoring;
using CamGuard.Api.Features.Streams;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Onvif;

/// <summary>
/// Una suscripción PullPoint por cámara ONVIF habilitada. El movimiento que informa la cámara hace de
/// "despertó/dormida" (como el sondeo TCP de las iCSee), así que Capture, Detection y Upload funcionan igual.
/// Al conectar descubre los perfiles de video y avisa a Streams si cambiaron. La manipulación se avisa por ntfy.
/// </summary>
public class OnvifEventService(
    IServiceScopeFactory scopeFactory,
    OnvifClient client,
    CameraSecretProtector secrets,
    CameraStatePublisher publisher,
    OnvifConnectionState connections,
    StreamSyncTrigger streamSync,
    INotifier notifier,
    IHubContext<EventsHub, IEventsClient> hub,
    IOptions<OnvifOptions> options,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<OnvifEventService> logger) : BackgroundService
{
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(1);

    /// <summary>Una manipulación ya avisada no se repite antes de este lapso (p. ej. tras reconectar).</summary>
    private static readonly TimeSpan TamperRepeat = TimeSpan.FromMinutes(30);

    private const int MessageLimit = 20;

    private readonly Dictionary<int, Worker> _workers = new();

    /// <param name="Fingerprint">Datos de conexión: si cambian, el bucle de la cámara se reinicia.</param>
    private sealed record Worker(Camera Camera, string Fingerprint, CancellationTokenSource Stop, Task Task, OnvifMotionTracker Tracker);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refresh = TimeSpan.FromSeconds(Math.Max(1, options.Value.CameraRefreshSeconds));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RefreshAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("No se pudo revisar las cámaras ONVIF: {Message}", ex.Message);
                }

                await Task.Delay(refresh, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var worker in _workers.Values) await StopAsync(worker, publish: false);
            _workers.Clear();
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        List<Camera> cameras;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cameras = await db.Cameras.AsNoTracking().Where(c => c.Enabled && c.Kind == CameraKind.Onvif).ToListAsync(ct);
        }

        var active = cameras.ToDictionary(c => c.Id);
        foreach (var (id, worker) in _workers.ToList())
        {
            if (active.TryGetValue(id, out var camera) && Fingerprint(camera) == worker.Fingerprint) continue;
            _workers.Remove(id);
            await StopAsync(worker, publish: true);
            if (!active.ContainsKey(id)) connections.Remove(id);
        }

        foreach (var camera in cameras.Where(c => !_workers.ContainsKey(c.Id)))
        {
            var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var tracker = new OnvifMotionTracker(TimeSpan.FromSeconds(options.Value.AsleepDelaySeconds));
            var task = Task.Run(() => RunCameraAsync(camera, tracker, stop.Token), CancellationToken.None);
            _workers[camera.Id] = new Worker(camera, Fingerprint(camera), stop, task, tracker);
        }
    }

    private static string Fingerprint(Camera c) => $"{c.Ip}|{c.OnvifPort}|{c.Username}|{c.PasswordProtected}";

    /// <summary>Corta el bucle de una cámara; si estaba grabando, la cierra (igual que al borrarla o pausarla).</summary>
    private async Task StopAsync(Worker worker, bool publish)
    {
        await worker.Stop.CancelAsync();
        try
        {
            await worker.Task;
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
        }
        worker.Stop.Dispose();

        if (!publish || worker.Tracker.Disconnect() is not { } change) return;
        try
        {
            await publisher.PublishAsync(worker.Camera, change, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Si la cámara se borró, el evento no se puede guardar; el resto ya se aplicó.
            logger.LogWarning("No se pudo registrar el cierre de la cámara {Name}: {Message}", worker.Camera.Name, ex.Message);
        }
    }

    private async Task RunCameraAsync(Camera camera, OnvifMotionTracker tracker, CancellationToken ct)
    {
        var password = secrets.TryUnprotect(camera.PasswordProtected);
        if (password is null)
        {
            connections.Disconnected(camera.Id, "Sin contraseña válida: escríbela en Configuración.");
            logger.LogWarning("La cámara ONVIF {Name} no tiene contraseña válida: escríbela en Configuración", camera.Name);
            return;
        }

        var credentials = new OnvifCredentials(camera.Username, password);
        DateTimeOffset? lastTamperNotice = null;
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(camera, credentials, tracker, () =>
                {
                    attempt = 0;
                    connections.Connected(camera.Id);
                }, at => lastTamperNotice = at, () => lastTamperNotice, ct);
                connections.Disconnected(camera.Id, "La cámara cerró la suscripción de eventos.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                connections.Disconnected(camera.Id, ex.Message);
                logger.LogWarning("Eventos ONVIF de {Name} interrumpidos: {Message}", camera.Name, ex.Message);
                // Varios intentos con una contraseña mala bloquean la cuenta de la cámara: se espera más.
                if (ex is OnvifException { Unauthorized: true }) attempt = Math.Max(attempt, 5);
            }

            if (tracker.Disconnect() is { } change)
                await PublishSafeAsync(camera, change, ct);

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

    private async Task ListenAsync(Camera camera, OnvifCredentials credentials, OnvifMotionTracker tracker, Action onConnected,
        Action<DateTimeOffset> tamperNotified, Func<DateTimeOffset?> lastTamperNotice, CancellationToken ct)
    {
        var o = options.Value;
        var session = await client.ConnectAsync(camera.Ip, camera.OnvifPort, credentials, ct);
        var capabilities = await client.GetCapabilitiesAsync(session, ct);

        try
        {
            await UpdateStreamsAsync(camera, session, capabilities, ct);
        }
        catch (Exception ex) when (ex is OnvifException && !ct.IsCancellationRequested)
        {
            // Sin video no se graba, pero los eventos siguen sirviendo (y quedan en el historial).
            logger.LogWarning("No se pudieron leer los perfiles de video de {Name}: {Message}", camera.Name, ex.Message);
        }

        var termination = TimeSpan.FromSeconds(Math.Max(30, o.SubscriptionSeconds));
        var subscription = await client.CreatePullPointAsync(session, capabilities, termination, ct);
        onConnected();
        logger.LogInformation("Escuchando eventos ONVIF de {Name}", camera.Name);

        var renewAt = clock.GetUtcNow() + termination / 2;
        var pullTimeout = TimeSpan.FromSeconds(Math.Max(1, o.PullTimeoutSeconds));
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Con un cierre pendiente se pregunta más seguido, para no alargar el clip de más.
                var timeout = tracker.TimeUntilAsleep(clock.GetUtcNow()) is { } left && left < pullTimeout
                    ? TimeSpan.FromSeconds(Math.Max(1, Math.Ceiling(left.TotalSeconds)))
                    : pullTimeout;

                var messages = await client.PullMessagesAsync(session, subscription, timeout, MessageLimit, ct);
                foreach (var message in messages)
                {
                    if (OnvifEventRules.Interpret(message) is not { } signal) continue;
                    if (signal.Kind == OnvifSignalKind.Motion)
                    {
                        if (tracker.Update(signal.Active, clock.GetUtcNow()) is { } change)
                            await publisher.PublishAsync(camera, change, ct);
                    }
                    else if (signal.Active)
                    {
                        var now = clock.GetUtcNow();
                        if (lastTamperNotice() is { } last && now - last < TamperRepeat) continue;
                        tamperNotified(now);
                        await HandleTamperAsync(camera, message.UtcTime ?? now, ct);
                    }
                }

                if (tracker.Tick(clock.GetUtcNow()) is { } asleep)
                    await publisher.PublishAsync(camera, asleep, ct);

                if (clock.GetUtcNow() >= renewAt)
                {
                    await client.RenewAsync(session, subscription, termination, ct);
                    renewAt = clock.GetUtcNow() + termination / 2;
                }
            }
        }
        finally
        {
            // Libera la suscripción en la cámara (tienen pocas); si no responde, vence sola.
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await client.UnsubscribeAsync(session, subscription, cts.Token);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>Guarda perfiles y códec si cambiaron, completa el modelo y pide registrar los streams en go2rtc.</summary>
    private async Task UpdateStreamsAsync(Camera camera, OnvifSession session, OnvifCapabilities capabilities, CancellationToken ct)
    {
        var streams = await client.DiscoverStreamsAsync(session, capabilities, ct);
        string? model = null;
        if (string.IsNullOrWhiteSpace(camera.Model))
        {
            try
            {
                model = (await client.GetDeviceInformationAsync(session, ct)).DisplayModel;
            }
            catch (OnvifException)
            {
            }
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Cameras.FindAsync([camera.Id], ct);
        if (stored is null) return;

        var changed = stored.OnvifMainStreamUri != streams.Main.Uri
                      || stored.OnvifSubStreamUri != streams.Sub.Uri
                      || stored.OnvifMainCodec != streams.Main.Codec;
        var modelChanged = model is not null && string.IsNullOrWhiteSpace(stored.Model);
        if (!changed && !modelChanged) return;

        stored.OnvifMainStreamUri = streams.Main.Uri;
        stored.OnvifSubStreamUri = streams.Sub.Uri;
        stored.OnvifMainCodec = streams.Main.Codec;
        if (modelChanged) stored.Model = model;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Cámara {Name}: principal {Main}, substream {Sub}", camera.Name, streams.Main.Describe(), streams.Sub.Describe());
        if (changed) streamSync.Request();
        if (modelChanged) await hub.Clients.All.CameraInfoUpdated(DeviceInfoService.ToMessage(stored));
    }

    private async Task HandleTamperAsync(Camera camera, DateTimeOffset at, CancellationToken ct)
    {
        logger.LogWarning("Cámara {Name}: manipulación detectada", camera.Name);
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CameraEvents.Add(new CameraEvent { CameraId = camera.Id, Type = CameraEventType.Tamper, OccurredAt = at.ToUniversalTime() });
            await db.SaveChangesAsync(ct);
        }

        // La bandeja de avisos nunca lanza: si ntfy no responde, se reintenta después.
        await notifier.NotifyAsync(new Notification(
            Title: $"Cámara manipulada: {camera.Name}",
            Message: $"La cámara detectó que la taparon o la movieron a las {at.ToLocalTime():HH:mm:ss}.",
            ClickUrl: appOptions.Value.PublicBaseUrl,
            Tags: ["warning"],
            Priority: 4), CancellationToken.None);
    }

    private async Task PublishSafeAsync(Camera camera, CameraEventType change, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(camera, change, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("No se pudo registrar el cierre de la cámara {Name}: {Message}", camera.Name, ex.Message);
        }
    }
}

public static class OnvifFeature
{
    public static IServiceCollection AddOnvifFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OnvifOptions>(configuration.GetSection(OnvifOptions.Section));
        services.AddHttpClient(OnvifClient.HttpClientName, c =>
        {
            // Cada petición tiene su propio tope (RequestTimeoutSeconds + la espera de PullMessages).
            c.Timeout = Timeout.InfiniteTimeSpan;
            c.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        });
        services.AddSingleton<OnvifClient>();
        services.AddSingleton<OnvifConnectionState>();
        services.AddHostedService<OnvifEventService>();
        return services;
    }
}
