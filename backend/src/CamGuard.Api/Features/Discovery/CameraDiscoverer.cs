using System.Diagnostics;
using System.Net.Sockets;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Devices;
using CamGuard.Api.Features.Monitoring;
using CamGuard.Api.Features.Onvif;
using CamGuard.Api.Features.Tuya;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Discovery;

/// <param name="Subnets">Subredes revisadas.</param>
/// <param name="Warnings">Lo que no se pudo revisar (subred inválida, Tuya sin credenciales…).</param>
public record DiscoveryResult(IReadOnlyList<DiscoveredCamera> Found, IReadOnlyList<string> Subnets, IReadOnlyList<string> Warnings,
    long ElapsedMs);

/// <summary>
/// Busca cámaras nuevas: barrido TCP de la LAN (la API está en la red bridge de Docker, así que no hay
/// multicast ni broadcast), confirmación con el protocolo real y listado de Tuya por la nube. Nunca prueba
/// contraseñas: un solo login DVRIP con clave vacía (identifica la cámara) y ONVIF sin autenticación.
/// Las cámaras ya registradas no se vuelven a contactar. Una búsqueda a la vez.
/// </summary>
public class CameraDiscoverer(
    IPortProbe probe,
    OnvifClient onvif,
    TuyaApiClient tuya,
    IOptions<DiscoveryOptions> options,
    ILogger<CameraDiscoverer> logger)
{
    /// <summary>Confirmaciones de protocolo simultáneas (cada una abre una conexión y espera respuesta).</summary>
    private const int ConfirmConcurrency = 16;

    private readonly SemaphoreSlim _busy = new(1, 1);

    /// <summary>Null si ya hay una búsqueda en curso.</summary>
    public async Task<DiscoveryResult?> TryRunAsync(IReadOnlyList<Camera> known, CancellationToken ct)
    {
        if (!await _busy.WaitAsync(0, ct)) return null;
        try
        {
            return await RunAsync(known, ct);
        }
        finally
        {
            _busy.Release();
        }
    }

    private async Task<DiscoveryResult> RunAsync(IReadOnlyList<Camera> known, CancellationToken ct)
    {
        var o = options.Value;
        var stopwatch = Stopwatch.StartNew();
        var lanCameraIps = known.Where(c => c.Kind != CameraKind.Tuya && !string.IsNullOrWhiteSpace(c.Ip)).Select(c => c.Ip.Trim()).ToList();
        var plan = SubnetPlanner.Plan(o.Subnets, lanCameraIps, o.MaxHosts);
        var warnings = plan.Warnings.ToList();

        // Las IPs registradas no se sondean: no se molesta a una cámara que ya se usa.
        var knownIps = lanCameraIps.ToHashSet(StringComparer.Ordinal);
        var hosts = plan.Hosts.Where(h => !knownIps.Contains(h)).ToList();

        var lanTask = ScanLanAsync(hosts, o, ct);
        var tuyaTask = ListTuyaAsync(known, warnings, ct);
        var hits = await lanTask;
        var devices = await tuyaTask;

        var found = DiscoveryMerge.Merge(hits, devices, known, o.TuyaCategorySet());
        stopwatch.Stop();
        logger.LogInformation("Búsqueda de cámaras: {Subnets} ({Hosts} equipos) y Tuya, {Found} nuevas en {Seconds:0.0} s",
            plan.Subnets.Count == 0 ? "sin subredes" : string.Join(", ", plan.Subnets), hosts.Count, found.Count,
            stopwatch.Elapsed.TotalSeconds);
        return new DiscoveryResult(found, plan.Subnets, warnings, stopwatch.ElapsedMilliseconds);
    }

    private async Task<IReadOnlyList<LanHit>> ScanLanAsync(IReadOnlyList<string> hosts, DiscoveryOptions o, CancellationToken ct)
    {
        if (hosts.Count == 0) return [];

        var probes = hosts.SelectMany(h => new[] { (Host: h, Protocol: LanProtocol.Dvrip, Port: o.DvripPort) }
                .Concat(o.OnvifPortList().Select(p => (Host: h, Protocol: LanProtocol.Onvif, Port: p))))
            .ToList();
        var timeout = TimeSpan.FromMilliseconds(o.ConnectTimeoutMs);

        var open = new List<(string Host, LanProtocol Protocol, int Port)>();
        await Parallel.ForEachAsync(probes, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, o.MaxConcurrency), CancellationToken = ct },
            async (p, token) =>
            {
                if (await probe.IsOpenAsync(p.Host, p.Port, timeout, token))
                    lock (open) open.Add(p);
            });

        var hits = new List<LanHit>();
        var confirmTimeout = TimeSpan.FromSeconds(o.ConfirmTimeoutSeconds);
        await Parallel.ForEachAsync(open, new ParallelOptions { MaxDegreeOfParallelism = ConfirmConcurrency, CancellationToken = ct },
            async (p, token) =>
            {
                LanHit? hit;
                try
                {
                    hit = p.Protocol == LanProtocol.Dvrip
                        ? await ConfirmDvripAsync(p.Host, p.Port, confirmTimeout, token)
                        : await ConfirmOnvifAsync(p.Host, p.Port, token);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    // Un equipo que responde algo inesperado (router, impresora…) no corta la búsqueda: no es una cámara.
                    logger.LogDebug("Búsqueda: {Host}:{Port} respondió algo inesperado ({Error})", p.Host, p.Port, ex.Message);
                    hit = null;
                }
                if (hit is not null)
                    lock (hits) hits.Add(hit);
            });
        return hits;
    }

    /// <summary>
    /// Un solo login con usuario "admin" y clave vacía: cualquier respuesta Sofia (aceptado o rechazado)
    /// confirma que es una iCSee. Si entra (cámara sin clave), se lee el modelo.
    /// </summary>
    private static async Task<LanHit?> ConfirmDvripAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await using var client = await DvripClient.ConnectAsync(host, port, timeout, ct);
            try
            {
                await client.LoginAsync("admin", "", ct);
            }
            catch (DvripException ex) when (ex.Ret is not null)
            {
                return new LanHit(host, LanProtocol.Dvrip, port);
            }

            string? model = null;
            try
            {
                model = DeviceInfoParser.ParseModel(await client.GetInfoAsync(DvripQueries.SystemInfo, ct));
            }
            catch (DvripException)
            {
            }
            return new LanHit(host, LanProtocol.Dvrip, port, model);
        }
        catch (Exception ex) when (ex is DvripException or SocketException or IOException or OperationCanceledException
                                   && !ct.IsCancellationRequested)
        {
            // Sin respuesta Sofia: el puerto lo usa otra cosa.
            return null;
        }
    }

    private async Task<LanHit?> ConfirmOnvifAsync(string host, int port, CancellationToken ct) =>
        await onvif.IdentifyAsync(host, port, ct) ? new LanHit(host, LanProtocol.Onvif, port) : null;

    private async Task<IReadOnlyList<TuyaListedDevice>> ListTuyaAsync(IReadOnlyList<Camera> known, List<string> warnings, CancellationToken ct)
    {
        if (!tuya.IsConfigured)
        {
            lock (warnings) warnings.Add("Tuya sin configurar (TUYA_ACCESS_ID y TUYA_ACCESS_SECRET): no se buscaron dispositivos Tuya.");
            return [];
        }

        try
        {
            // Respaldo si el proyecto no permite listar por cuentas vinculadas: el usuario dueño de una Tuya ya registrada.
            string? uid = null;
            if (known.FirstOrDefault(c => c.Kind == CameraKind.Tuya && !string.IsNullOrWhiteSpace(c.TuyaDeviceId)) is { } existing)
            {
                try
                {
                    uid = (await tuya.GetDeviceAsync(existing.TuyaDeviceId!, ct)).Uid;
                }
                catch (TuyaApiException)
                {
                }
            }
            return await tuya.ListDevicesAsync(uid, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("No se pudo listar los dispositivos Tuya: {Message}", ex.Message);
            lock (warnings) warnings.Add($"No se pudo listar los dispositivos Tuya: {ex.Message}");
            return [];
        }
    }
}

public static class DiscoveryFeature
{
    public static IServiceCollection AddDiscoveryFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DiscoveryOptions>(configuration.GetSection(DiscoveryOptions.Section));
        services.AddSingleton<CameraDiscoverer>();
        return services;
    }
}
