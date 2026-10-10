using System.Text.Json;
using CamGuard.Api.Common;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Streams;

public class StreamsOptions
{
    public const string Section = "Streams";

    public int SyncSeconds { get; set; } = 30;
}

/// <summary>Cliente de /api/streams de go2rtc.</summary>
public class Go2rtcStreamsClient(HttpClient http)
{
    /// <returns>Stream → origen; el origen es null cuando go2rtc tiene la conexión abierta y no lo informa.</returns>
    public async Task<Dictionary<string, string?>> GetStreamsAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("api/streams", ct);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);

        var streams = new Dictionary<string, string?>();
        if (json.RootElement.ValueKind != JsonValueKind.Object) return streams;

        foreach (var stream in json.RootElement.EnumerateObject())
        {
            string? url = null;
            if (stream.Value.ValueKind == JsonValueKind.Object
                && stream.Value.TryGetProperty("producers", out var producers)
                && producers.ValueKind == JsonValueKind.Array
                && producers.GetArrayLength() > 0
                && producers[0].ValueKind == JsonValueKind.Object
                && producers[0].TryGetProperty("url", out var u)
                && u.ValueKind == JsonValueKind.String)
            {
                url = u.GetString();
            }

            streams[stream.Name] = url;
        }

        return streams;
    }

    /// <summary>
    /// PATCH crea el stream o cambia su origen sin cortar una conexión abierta, y a diferencia
    /// de PUT no intenta escribir go2rtc.yaml (montado de solo lectura).
    /// </summary>
    public async Task PatchStreamAsync(string name, string source, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            $"api/streams?name={Uri.EscapeDataString(name)}&src={Uri.EscapeDataString(source)}");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // El cuerpo es el mensaje de error de go2rtc; no se registra el origen porque lleva la contraseña.
            var error = (await response.Content.ReadAsStringAsync(ct)).Trim();
            throw new InvalidOperationException($"go2rtc rechazó el stream {name}: {(int)response.StatusCode} {error}");
        }
    }

    /// <summary>
    /// go2rtc quita el stream de memoria y luego intenta escribir go2rtc.yaml; como es de solo
    /// lectura puede responder error aunque el stream ya no exista. La siguiente sincronización lo verifica.
    /// </summary>
    public async Task DeleteStreamAsync(string name, CancellationToken ct)
    {
        using var response = await http.DeleteAsync($"api/streams?src={Uri.EscapeDataString(name)}", ct);
    }
}

/// <summary>Permite a los endpoints de cámaras pedir una sincronización inmediata.</summary>
public class StreamSyncTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Request()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya había una sincronización pendiente.
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Mantiene en go2rtc un stream por cámara habilitada. Los streams agregados por la API viven
/// en memoria, así que se vuelven a registrar periódicamente (p. ej. tras reiniciar go2rtc).
/// </summary>
public class StreamSyncService(
    IServiceScopeFactory scopeFactory,
    Go2rtcStreamsClient go2rtc,
    CameraSecretProtector secrets,
    StreamSyncTrigger trigger,
    IOptions<StreamsOptions> options,
    ILogger<StreamSyncService> logger) : BackgroundService
{
    private readonly Dictionary<string, string> _applied = new();
    private readonly HashSet<int> _warnedWithoutPassword = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.SyncSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("No se pudo sincronizar los streams con go2rtc: {Message}", ex.Message);
            }

            try
            {
                await trigger.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        List<Camera> cameras;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cameras = await db.Cameras.AsNoTracking().Where(c => c.Enabled).ToListAsync(ct);
        }

        var desired = new Dictionary<string, string>();
        foreach (var camera in cameras)
        {
            var password = secrets.TryUnprotect(camera.PasswordProtected);
            if (password is null)
            {
                if (_warnedWithoutPassword.Add(camera.Id))
                    logger.LogWarning("La cámara {Name} no tiene contraseña válida: escríbela en Configuración", camera.Name);
                continue;
            }

            _warnedWithoutPassword.Remove(camera.Id);
            desired[camera.Go2rtcStream] = Go2rtcStreamSource.For(camera.Ip, camera.DvripPort, camera.Username, password);
        }

        var current = await go2rtc.GetStreamsAsync(ct);
        var plan = StreamSyncPlan.Compute(desired, current, _applied);

        foreach (var (name, source) in plan.ToPatch)
        {
            try
            {
                await go2rtc.PatchStreamAsync(name, source, ct);
                _applied[name] = source;
                logger.LogInformation("Stream {Name} registrado en go2rtc", name);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("No se pudo registrar el stream {Name}: {Message}", name, ex.Message);
            }
        }

        foreach (var name in plan.ToDelete)
        {
            await go2rtc.DeleteStreamAsync(name, ct);
            _applied.Remove(name);
            logger.LogInformation("Stream {Name} quitado de go2rtc", name);
        }
    }
}

public static class StreamsFeature
{
    public static IServiceCollection AddStreamsFeature(this IServiceCollection services, IConfiguration configuration)
    {
        var go2rtc = configuration.GetSection(Go2rtcOptions.Section).Get<Go2rtcOptions>() ?? new Go2rtcOptions();
        services.Configure<StreamsOptions>(configuration.GetSection(StreamsOptions.Section));
        services.AddHttpClient<Go2rtcStreamsClient>(go2rtc.ConfigureHttpClient);
        services.AddSingleton<StreamSyncTrigger>();
        services.AddHostedService<StreamSyncService>();
        return services;
    }
}
