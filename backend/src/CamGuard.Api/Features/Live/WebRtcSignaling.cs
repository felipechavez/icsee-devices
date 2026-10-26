using System.Text;
using CamGuard.Api.Common;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Features.Monitoring;
using CamGuard.Api.Features.Tuya;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Features.Live;

/// <summary>
/// Proxy de señalización WebRTC: el navegador manda su oferta SDP aquí y la API
/// la reenvía a go2rtc, que no queda expuesto. El video viaja directo go2rtc → navegador.
/// Un dispositivo Tuya se puede ver a demanda: se registra una URL nueva en go2rtc y eso lo despierta.
/// </summary>
public static class WebRtcSignaling
{
    public const int MaxOfferBytes = 64 * 1024;

    public class Go2rtcClient(HttpClient http)
    {
        /// <returns>La respuesta SDP, o null si go2rtc no pudo abrir la cámara.</returns>
        public async Task<string?> ExchangeSdpAsync(string stream, string offer, CancellationToken ct)
        {
            using var content = new StringContent(offer, Encoding.UTF8, "application/sdp");
            using var response = await http.PostAsync($"api/webrtc?src={Uri.EscapeDataString(stream)}", content, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }
    }

    public static IServiceCollection AddLiveFeature(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(Go2rtcOptions.Section).Get<Go2rtcOptions>() ?? new Go2rtcOptions();
        services.AddHttpClient<Go2rtcClient>(c =>
        {
            options.ConfigureHttpClient(c);
            // go2rtc espera a que la cámara entregue el primer cuadro antes de responder;
            // un dispositivo Tuya dormido tarda hasta ~40 s.
            c.Timeout = TimeSpan.FromSeconds(90);
        });
        return services;
    }

    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/live/{cameraId:int}/webrtc", async (
                    int cameraId,
                    HttpRequest request,
                    AppDbContext db,
                    CameraStateStore states,
                    Go2rtcClient go2rtc,
                    TuyaStreamPreparer tuyaStream,
                    ILogger<Go2rtcClient> logger,
                    CancellationToken ct) =>
                {
                    var camera = await db.Cameras.FindAsync([cameraId], ct);
                    if (camera is null)
                        return Results.NotFound();
                    var tuya = camera.Kind == CameraKind.Tuya;
                    if (!tuya && !states.Get(cameraId).IsAwake)
                        return Results.Problem("La cámara está en reposo.", statusCode: StatusCodes.Status503ServiceUnavailable);

                    if (request.ContentLength is > MaxOfferBytes)
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

                    using var reader = new StreamReader(request.Body, Encoding.UTF8);
                    var offer = await reader.ReadToEndAsync(ct);
                    if (!offer.StartsWith("v=0", StringComparison.Ordinal) || offer.Length > MaxOfferBytes)
                        return Results.BadRequest(new { message = "Se esperaba una oferta SDP." });

                    if (tuya)
                    {
                        try
                        {
                            await tuyaStream.PrepareAsync(camera, ct);
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            logger.LogWarning("No se pudo preparar el video Tuya de {Name}: {Message}", camera.Name, ex.Message);
                            return Results.Problem("No se pudo pedir el video a Tuya.", statusCode: StatusCodes.Status502BadGateway);
                        }
                    }

                    var answer = await go2rtc.ExchangeSdpAsync(camera.Go2rtcStream, offer, ct);
                    return answer is null
                        ? Results.Problem("go2rtc no pudo conectar con la cámara.", statusCode: StatusCodes.Status502BadGateway)
                        : Results.Text(answer, "application/sdp");
                })
                .Accepts<string>("application/sdp")
                .WithTags("Live");
        }
    }
}
