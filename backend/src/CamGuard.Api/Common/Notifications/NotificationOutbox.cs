using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Common.Notifications;

/// <summary>Aviso en espera de enviarse a ntfy.</summary>
public record OutboxItem(Guid Id, DateTimeOffset CreatedAt, Notification Notification, int Attempts = 0);

/// <summary>
/// Bandeja persistente de avisos: un JSON por aviso en {DataDirectory}/notify-queue, escrito con temporal +
/// rename (como la cola de subida). El nombre empieza con la hora de creación, así el orden de los archivos es
/// el de envío. Tope de <see cref="MaxItems"/>: al pasarlo se descartan los más viejos.
/// </summary>
public class NotificationOutbox
{
    public const int DefaultMaxItems = 500;

    private readonly string _directory;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Lock _lock = new();

    public NotificationOutbox(IOptions<AppOptions> options) : this(Path.Combine(options.Value.DataDirectory, "notify-queue"))
    {
    }

    public NotificationOutbox(string directory, int maxItems = DefaultMaxItems)
    {
        _directory = directory;
        MaxItems = maxItems;
        Directory.CreateDirectory(_directory);
    }

    public int MaxItems { get; }

    public int Count => Files().Length;

    public bool IsEmpty => Count == 0;

    /// <summary>Guarda el aviso y despierta al servicio de envío. Devuelve cuántos viejos se descartaron por el tope.</summary>
    public async Task<int> AddAsync(OutboxItem item, CancellationToken ct)
    {
        await SaveAsync(item, ct);
        var dropped = Trim();
        _signal.Release();
        return dropped;
    }

    /// <summary>Escribe (o reescribe, con más intentos) el aviso.</summary>
    public async Task SaveAsync(OutboxItem item, CancellationToken ct)
    {
        var path = PathFor(item);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(item), ct);
        File.Move(temp, path, overwrite: true);
    }

    public void Remove(OutboxItem item) => File.Delete(PathFor(item));

    /// <summary>Todos los avisos en orden de creación. Un JSON ilegible se borra.</summary>
    public async Task<IReadOnlyList<OutboxItem>> GetAllAsync(CancellationToken ct)
    {
        var items = new List<OutboxItem>();
        foreach (var file in Files())
        {
            OutboxItem? item = null;
            try
            {
                item = JsonSerializer.Deserialize<OutboxItem>(await File.ReadAllTextAsync(file, ct));
            }
            catch (JsonException)
            {
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            if (item is null) File.Delete(file);
            else items.Add(item);
        }

        return items.OrderBy(i => i.CreatedAt).ToList();
    }

    /// <summary>Espera un aviso nuevo o hasta que pase el intervalo.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct) => await _signal.WaitAsync(timeout, ct);

    private int Trim()
    {
        lock (_lock)
        {
            var files = Files();
            var excess = files.Length - MaxItems;
            for (var i = 0; i < excess; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch (IOException)
                {
                }
            }
            return Math.Max(0, excess);
        }
    }

    /// <summary>Ordenados por nombre: primero los más viejos.</summary>
    private string[] Files()
    {
        var files = Directory.GetFiles(_directory, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    private string PathFor(OutboxItem item) =>
        Path.Combine(_directory, $"{item.CreatedAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture)}-{item.Id:N}.json");
}

/// <summary>Regla del envío atrasado (pura).</summary>
public static class DelayedNotification
{
    /// <summary>Un aviso retrasado más que esto lleva la hora original en el mensaje.</summary>
    public static readonly TimeSpan PrefixAfter = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Null si el aviso caducó (más viejo que <paramref name="maxDelay"/>). Si se retrasó más de 2 min, antepone
    /// "Retrasado (hh:mm) · " con la hora local de creación. Prioridad, imagen y enlace se mantienen.
    /// </summary>
    public static Notification? Prepare(Notification n, DateTimeOffset createdAt, DateTimeOffset now, TimeSpan maxDelay, TimeZoneInfo? zone = null)
    {
        var delay = now - createdAt;
        if (delay > maxDelay) return null;
        if (delay <= PrefixAfter) return n;

        var local = TimeZoneInfo.ConvertTime(createdAt, zone ?? TimeZoneInfo.Local);
        return n with { Message = $"Retrasado ({local.ToString("HH:mm", CultureInfo.InvariantCulture)}) · {n.Message}" };
    }
}

/// <param name="FailingSince">Primer fallo desde el último envío exitoso; null = el último salió bien.</param>
public record NotifierResult(DateTimeOffset? LastSuccessAt, DateTimeOffset? FailingSince, string? LastError);

/// <summary>Resultado de los envíos a ntfy, leído por Features/Health (solo se muestra; no abre problema).</summary>
public class NotifierHealth(TimeProvider clock)
{
    private NotifierResult _current = new(null, null, null);

    public NotifierResult Current => Volatile.Read(ref _current);

    public void Succeeded() => Volatile.Write(ref _current, new NotifierResult(clock.GetUtcNow(), null, null));

    public void Failed(string error)
    {
        var current = Current;
        Volatile.Write(ref _current, current with { FailingSince = current.FailingSince ?? clock.GetUtcNow(), LastError = error });
    }
}

/// <summary>
/// El <see cref="INotifier"/> que usa toda la app. Si la bandeja está vacía intenta enviar al instante; si falla,
/// o si ya hay avisos esperando (para no desordenarlos), lo guarda y vuelve sin lanzar error.
/// <see cref="NotificationOutboxService"/> lo envía al volver ntfy.
/// </summary>
public class OutboxNotifier(
    IServiceProvider services,
    NotificationOutbox outbox,
    NotifierHealth health,
    TimeProvider clock,
    ILogger<OutboxNotifier> logger) : INotifier
{
    public async Task NotifyAsync(Notification notification, CancellationToken ct)
    {
        var item = new OutboxItem(Guid.NewGuid(), clock.GetUtcNow(), notification);
        if (outbox.IsEmpty)
        {
            try
            {
                await services.GetRequiredService<NtfyNotifier>().NotifyAsync(notification, ct);
                health.Succeeded();
                return;
            }
            catch (Exception ex)
            {
                health.Failed(ex.Message);
                logger.LogWarning("No se pudo enviar el aviso \"{Title}\" (queda en la bandeja): {Message}", notification.Title, ex.Message);
            }
        }

        // Se guarda aunque se esté apagando: sale al volver a arrancar.
        var dropped = await outbox.AddAsync(item, CancellationToken.None);
        if (dropped > 0)
            logger.LogWarning("Bandeja de avisos llena: se descartaron {Count} avisos antiguos", dropped);
    }
}

/// <summary>Envía la bandeja en orden de creación, con backoff; descarta lo que caducó.</summary>
public class NotificationOutboxService(
    IServiceProvider services,
    NotificationOutbox outbox,
    NotifierHealth health,
    IOptions<NtfyOptions> options,
    TimeProvider clock,
    ILogger<NotificationOutboxService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (clock.GetUtcNow() >= _pausedUntil) await SendPendingAsync(stoppingToken);
                await outbox.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en el envío de la bandeja de avisos");
                await Task.Delay(PollInterval, clock, stoppingToken);
            }
        }
    }

    private async Task SendPendingAsync(CancellationToken ct)
    {
        var maxDelay = TimeSpan.FromHours(Math.Max(0, options.Value.MaxDelayHours));
        foreach (var item in await outbox.GetAllAsync(ct))
        {
            var now = clock.GetUtcNow();
            var prepared = DelayedNotification.Prepare(item.Notification, item.CreatedAt, now, maxDelay);
            if (prepared is null)
            {
                logger.LogWarning("Aviso \"{Title}\" descartado: lleva más de {Hours} h sin poder enviarse", item.Notification.Title, options.Value.MaxDelayHours);
                outbox.Remove(item);
                continue;
            }

            try
            {
                await services.GetRequiredService<NtfyNotifier>().NotifyAsync(prepared, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                health.Failed(ex.Message);
                var attempts = item.Attempts + 1;
                var delay = RetryPolicy.Delay(attempts);
                _pausedUntil = now + delay;
                await outbox.SaveAsync(item with { Attempts = attempts }, ct);
                logger.LogWarning("No se pudo enviar el aviso \"{Title}\" (intento {Attempt}); se reintenta en {Delay}: {Message}",
                    item.Notification.Title, attempts, delay, ex.Message);
                return;
            }

            health.Succeeded();
            outbox.Remove(item);
            logger.LogInformation("Aviso atrasado enviado: {Title}", item.Notification.Title);
        }
    }
}

public static class NotificationsSetup
{
    public static IServiceCollection AddNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NtfyOptions>(configuration.GetSection(NtfyOptions.Section));
        // Un ntfy colgado no debe frenar a quien avisa: el aviso queda en la bandeja.
        services.AddHttpClient<NtfyNotifier>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<NotificationOutbox>();
        services.AddSingleton<NotifierHealth>();
        services.AddSingleton<INotifier, OutboxNotifier>();
        services.AddHostedService<NotificationOutboxService>();
        return services;
    }
}
