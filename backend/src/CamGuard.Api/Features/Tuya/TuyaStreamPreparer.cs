using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Streams;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Tuya;

/// <summary>
/// Las URLs de video de Tuya caducan y go2rtc no admite por API fuentes que las pidan solas
/// (expr:/exec:/echo: son "insecure"). Por eso, justo antes de usar el stream (grabar o ver en vivo),
/// se pide una URL RTSPS nueva y se registra en go2rtc. Pedir la URL despierta el dispositivo.
/// Tuya admite una sola sesión de video por dispositivo: si go2rtc ya está recibiendo, se reutiliza;
/// si la conexión quedó colgada (sin datos), se quita antes de pedir otra.
/// No registres la URL: lleva credenciales temporales.
/// </summary>
public class TuyaStreamPreparer(
    TuyaApiClient api,
    Go2rtcStreamsClient go2rtc,
    IOptions<TuyaOptions> options,
    TimeProvider clock,
    ILogger<TuyaStreamPreparer> logger)
{
    /// <summary>Lapso para comprobar que la conexión abierta sigue recibiendo datos.</summary>
    private static readonly TimeSpan ActivityCheck = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// go2rtc espera 5 s por defecto a que lleguen datos, y un dispositivo dormido tarda ~30 s en
    /// entregar el primer cuadro: <c>#timeout</c> alarga esa espera (y, de paso, desactiva el backchannel).
    /// </summary>
    public static string SourceFor(string url, bool viaFfmpeg, int timeoutSeconds) =>
        viaFfmpeg ? $"ffmpeg:{url}#video=copy#audio=copy" : $"{url}#timeout={timeoutSeconds}";

    /// <param name="forceNew">
    /// Siempre una sesión nueva. Para grabar tras un evento: un timbre hace que la cerradura abra su propia
    /// llamada con la app y la sesión anterior muere, aunque un instante antes siguiera recibiendo datos.
    /// </param>
    public async Task PrepareAsync(Camera camera, CancellationToken ct, bool forceNew = false)
    {
        if (camera is not { Kind: CameraKind.Tuya, TuyaDeviceId: { } deviceId })
            throw new InvalidOperationException($"La cámara {camera.Name} no es un dispositivo Tuya.");

        if (!forceNew && await IsReceivingAsync(camera.Go2rtcStream, ct))
        {
            logger.LogInformation("Stream {Stream} de {Name} (Tuya) ya activo: se reutiliza", camera.Go2rtcStream, camera.Name);
            return;
        }

        // Una conexión colgada retendría la única sesión de Tuya y los nuevos clientes se engancharían a ella.
        await go2rtc.DeleteStreamAsync(camera.Go2rtcStream, ct);

        var url = await api.AllocateStreamUrlAsync(deviceId, "rtsp", ct);
        var o = options.Value;
        await go2rtc.PatchStreamAsync(camera.Go2rtcStream, SourceFor(url, o.StreamViaFfmpeg, o.StartupGraceSeconds), ct);
        logger.LogInformation("Stream {Stream} de {Name} (Tuya) listo en go2rtc", camera.Go2rtcStream, camera.Name);
    }

    private async Task<bool> IsReceivingAsync(string stream, CancellationToken ct)
    {
        if (await go2rtc.GetReceivedBytesAsync(stream, ct) is not { } before) return false;
        await Task.Delay(ActivityCheck, clock, ct);
        return await go2rtc.GetReceivedBytesAsync(stream, ct) is { } after && after > before;
    }
}
