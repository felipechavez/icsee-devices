using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Common.Security;
using CamGuard.Api.Features.Tuya;

namespace CamGuard.Api.Features.Devices;

/// <summary>"Probar conexión" desde Configuración: inicio de sesión DVRIP con los datos del formulario.</summary>
public static class TestConnection
{
    /// <param name="CameraId">Si la contraseña viene vacía, se usa la guardada de esta cámara.</param>
    /// <param name="TuyaDeviceId">Solo para <see cref="CameraKind.Tuya"/>: se consulta por la Tuya Cloud API.</param>
    public record Request(int? CameraId, string Ip, int DvripPort, string Username, string? Password,
        CameraKind Kind = CameraKind.Icsee, string? TuyaDeviceId = null);

    public record Response(bool Ok, string Message, string? Model);

    public static Dictionary<string, string[]> Validate(Request r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.Kind == CameraKind.Tuya)
        {
            if (string.IsNullOrWhiteSpace(r.TuyaDeviceId))
                errors[nameof(r.TuyaDeviceId)] = ["El Device ID de Tuya es obligatorio."];
            return errors;
        }
        if (!IPAddress.TryParse(r.Ip, out _))
            errors[nameof(r.Ip)] = ["IP inválida."];
        if (r.DvripPort is < 1 or > 65535)
            errors[nameof(r.DvripPort)] = ["Puerto fuera de rango."];
        if (string.IsNullOrWhiteSpace(r.Username))
            errors[nameof(r.Username)] = ["El usuario es obligatorio."];
        return errors;
    }

    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/cameras/test", async (
                    Request request,
                    AppDbContext db,
                    CameraSecretProtector secrets,
                    DvripConnector connector,
                    TuyaApiClient tuya,
                    CancellationToken ct) =>
                {
                    var errors = Validate(request);
                    if (errors.Count > 0)
                        return Results.ValidationProblem(errors);

                    if (request.Kind == CameraKind.Tuya)
                        return Results.Ok(await TestTuyaAsync(tuya, request.TuyaDeviceId!.Trim(), ct));

                    var password = request.Password;
                    if (string.IsNullOrEmpty(password) && request.CameraId is { } id)
                        password = secrets.TryUnprotect((await db.Cameras.FindAsync([id], ct))?.PasswordProtected);
                    if (string.IsNullOrEmpty(password))
                        return Results.Ok(new Response(false, "Escribe la contraseña de la cámara.", null));

                    try
                    {
                        await using var client = await connector.OpenAsync(request.Ip.Trim(), request.DvripPort, request.Username.Trim(), password, ct);
                        string? model = null;
                        try
                        {
                            model = DeviceInfoParser.ParseModel(await client.GetInfoAsync(DvripQueries.SystemInfo, ct));
                        }
                        catch (DvripException)
                        {
                        }
                        return Results.Ok(new Response(true, "Conexión OK", model));
                    }
                    catch (DvripException ex)
                    {
                        return Results.Ok(new Response(false, ex.Message, null));
                    }
                    catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException && !ct.IsCancellationRequested)
                    {
                        return Results.Ok(new Response(false, "Sin respuesta: la cámara está dormida o fuera de la red.", null));
                    }
                })
                .WithTags("Cameras");
        }

        private static async Task<Response> TestTuyaAsync(TuyaApiClient tuya, string deviceId, CancellationToken ct)
        {
            if (!tuya.IsConfigured)
                return new Response(false, "Faltan las credenciales de Tuya (TUYA_ACCESS_ID y TUYA_ACCESS_SECRET en .env).", null);
            try
            {
                var device = await tuya.GetDeviceAsync(deviceId, ct);
                return new Response(true, $"Conexión OK · {(device.Online ? "en línea" : "fuera de línea")}", device.ProductName);
            }
            catch (TuyaApiException ex)
            {
                return new Response(false, ex.Message, null);
            }
            catch (HttpRequestException)
            {
                return new Response(false, "Sin respuesta de la Tuya Cloud API.", null);
            }
        }
    }
}

/// <summary>
/// Solo en Development: respuestas DVRIP crudas de una cámara despierta, para descubrir qué
/// claves entrega su firmware (ver DvripQueries y poc/README.md). No incluye credenciales.
/// </summary>
public static class DvripProbe
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment()) return;

            app.MapGet("/cameras/{id:int}/dvrip-probe", async (int id, AppDbContext db, DvripConnector connector, CancellationToken ct) =>
                {
                    var camera = await db.Cameras.FindAsync([id], ct);
                    if (camera is null)
                        return Results.NotFound();

                    await using var client = await connector.OpenAsync(camera, ct);
                    var result = new JsonObject();
                    foreach (var name in new[] { DvripQueries.SystemInfo, DvripQueries.StorageInfo }.Concat(DvripQueries.Battery))
                        result[$"info:{name}"] = await Capture(() => client.GetInfoAsync(name, ct));
                    foreach (var name in DvripQueries.Battery.Concat(DvripQueries.Detection))
                        result[$"config:{name}"] = await Capture(() => client.GetConfigAsync(name, ct));
                    return Results.Text(result.ToJsonString(), "application/json");
                })
                .WithTags("Cameras");
        }

        private static async Task<JsonNode?> Capture(Func<Task<JsonObject>> query)
        {
            try
            {
                return await query();
            }
            catch (DvripException ex)
            {
                return JsonValue.Create($"error: {ex.Message}");
            }
        }
    }
}
