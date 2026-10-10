using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Common.Security;

namespace CamGuard.Api.Features.Devices;

/// <summary>"Probar conexión" desde Configuración: inicio de sesión DVRIP con los datos del formulario.</summary>
public static class TestConnection
{
    /// <param name="CameraId">Si la contraseña viene vacía, se usa la guardada de esta cámara.</param>
    public record Request(int? CameraId, string Ip, int DvripPort, string Username, string? Password);

    public record Response(bool Ok, string Message, string? Model);

    public static Dictionary<string, string[]> Validate(Request r)
    {
        var errors = new Dictionary<string, string[]>();
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
                    CancellationToken ct) =>
                {
                    var errors = Validate(request);
                    if (errors.Count > 0)
                        return Results.ValidationProblem(errors);

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
