using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CamGuard.Api.Features.Devices;

/// <summary>Error de protocolo o respuesta con código distinto de OK.</summary>
public class DvripException(string message, int? ret = null) : Exception(message)
{
    public int? Ret { get; } = ret;
}

/// <summary>
/// Cliente mínimo del protocolo DVRIP/Sofia (XM) de las cámaras iCSee, puerto 34567.
/// Cada mensaje es una cabecera de 20 bytes seguida de JSON terminado en "\n\0".
/// Solo sirve con la cámara despierta: dormida no abre el puerto.
/// </summary>
public sealed class DvripClient : IAsyncDisposable
{
    public const int HeaderSize = 20;
    public const int MaxPayloadBytes = 1024 * 1024;

    public const ushort LoginRequest = 1000;
    public const ushort LogoutRequest = 1002;
    public const ushort ConfigSetRequest = 1040;
    public const ushort ConfigGetRequest = 1042;
    public const ushort InfoRequest = 1020;

    /// <summary>Códigos "Ret" de la cámara.</summary>
    public const int RetOk = 100;
    public static readonly int[] RetBadCredentials = [203, 205, 206];

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly TimeSpan _timeout;
    private uint _session;
    private uint _sequence;

    private DvripClient(TcpClient tcp, TimeSpan timeout)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _timeout = timeout;
    }

    public static async Task<DvripClient> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await tcp.ConnectAsync(host, port, cts.Token);
            return new DvripClient(tcp, timeout);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public async Task LoginAsync(string username, string password, CancellationToken ct)
    {
        var response = await SendAsync(LoginRequest, new JsonObject
        {
            ["EncryptType"] = "MD5",
            ["LoginType"] = "DVRIP-Web",
            ["PassWord"] = SofiaHash(password),
            ["UserName"] = username
        }, ct);

        var ret = RetOf(response);
        if (ret != RetOk)
            throw new DvripException(RetBadCredentials.Contains(ret ?? 0) ? "Usuario o contraseña incorrectos." : $"La cámara rechazó el inicio de sesión ({ret}).", ret);

        _session = ParseSession(response["SessionID"]?.GetValue<string>());
    }

    /// <summary>Información de solo lectura (p. ej. "SystemInfo", "StorageInfo").</summary>
    public Task<JsonObject> GetInfoAsync(string name, CancellationToken ct) => NamedAsync(InfoRequest, name, ct);

    public Task<JsonObject> GetConfigAsync(string name, CancellationToken ct) => NamedAsync(ConfigGetRequest, name, ct);

    public async Task SetConfigAsync(string name, JsonNode value, CancellationToken ct)
    {
        var response = await SendAsync(ConfigSetRequest, new JsonObject
        {
            ["Name"] = name,
            [name] = value.DeepClone(),
            ["SessionID"] = FormatSession(_session)
        }, ct);
        var ret = RetOf(response);
        if (ret != RetOk)
            throw new DvripException($"La cámara rechazó la configuración {name} ({ret}).", ret);
    }

    private async Task<JsonObject> NamedAsync(ushort messageId, string name, CancellationToken ct)
    {
        var response = await SendAsync(messageId, new JsonObject { ["Name"] = name, ["SessionID"] = FormatSession(_session) }, ct);
        var ret = RetOf(response);
        if (ret != RetOk)
            throw new DvripException($"La cámara no entrega {name} ({ret}).", ret);
        return response;
    }

    private async Task<JsonObject> SendAsync(ushort messageId, JsonObject payload, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        var frame = EncodeFrame(_session, _sequence++, messageId, payload.ToJsonString());
        await _stream.WriteAsync(frame, cts.Token);

        var header = new byte[HeaderSize];
        await _stream.ReadExactlyAsync(header, cts.Token);
        var (_, _, length) = DecodeHeader(header);
        if (length > MaxPayloadBytes)
            throw new DvripException("Respuesta DVRIP demasiado grande.");

        var body = new byte[length];
        await _stream.ReadExactlyAsync(body, cts.Token);
        return ParsePayload(body);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_session != 0)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var frame = EncodeFrame(_session, _sequence++, LogoutRequest,
                    new JsonObject { ["Name"] = "", ["SessionID"] = FormatSession(_session) }.ToJsonString());
                await _stream.WriteAsync(frame, cts.Token);
            }
        }
        catch (Exception)
        {
            // El cierre es de cortesía: si la cámara ya se durmió no hay a quién avisar.
        }
        finally
        {
            _tcp.Dispose();
        }
    }

    // --- Lógica pura (probada en CamGuard.Tests) ---

    /// <summary>Hash de contraseña de Sofia: MD5 reducido a 8 caracteres [0-9A-Za-z].</summary>
    public static string SofiaHash(string password)
    {
        var md5 = MD5.HashData(Encoding.UTF8.GetBytes(password));
        var chars = new char[8];
        for (var i = 0; i < 8; i++)
        {
            var n = (md5[2 * i] + md5[2 * i + 1]) % 62;
            chars[i] = n < 10 ? (char)('0' + n) : n < 36 ? (char)('A' + n - 10) : (char)('a' + n - 36);
        }
        return new string(chars);
    }

    public static byte[] EncodeFrame(uint session, uint sequence, ushort messageId, string json)
    {
        var body = Encoding.UTF8.GetBytes(json + "\n\0");
        var frame = new byte[HeaderSize + body.Length];
        frame[0] = 0xFF;
        frame[1] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), session);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8), sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(14), messageId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16), (uint)body.Length);
        body.CopyTo(frame, HeaderSize);
        return frame;
    }

    public static (uint Session, ushort MessageId, int Length) DecodeHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize || header[0] != 0xFF)
            throw new DvripException("Cabecera DVRIP inválida.");
        return (BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[14..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(header[16..]));
    }

    /// <summary>El JSON viene terminado en "\n\0" (a veces con basura después del cero).</summary>
    public static JsonObject ParsePayload(ReadOnlySpan<byte> body)
    {
        var end = body.IndexOf((byte)0);
        var text = Encoding.UTF8.GetString(end >= 0 ? body[..end] : body).Trim();
        try
        {
            return JsonNode.Parse(text) as JsonObject ?? throw new DvripException("Respuesta DVRIP sin objeto JSON.");
        }
        catch (JsonException)
        {
            throw new DvripException("Respuesta DVRIP con JSON inválido.");
        }
    }

    public static int? RetOf(JsonObject response) =>
        response["Ret"] is JsonValue v && v.TryGetValue<int>(out var ret) ? ret : null;

    /// <summary>"0x0000000B" → 11.</summary>
    public static uint ParseSession(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0;
    }

    public static string FormatSession(uint session) =>
        "0x" + session.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
}
