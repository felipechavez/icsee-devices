// Escucha el Message Service de Tuya (Pulsar por WebSocket) y muestra cada reporte de DPs.
// Uso: dotnet run poc/tuya/watch-messages.cs        (lee poc/tuya/tuya.poc.json)
// Requiere activar "Message Service" en el proyecto de Tuya Developer Platform.
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

var configPath = args.FirstOrDefault() ?? "poc/tuya/tuya.poc.json";
var config = JsonNode.Parse(File.ReadAllText(configPath))!;
var accessId = (string)config["clientId"]!;
var accessSecret = (string)config["clientSecret"]!;
var mqEndpoint = (string?)config["messageEndpoint"] ?? "wss://mqe.tuyaus.com:8285/";
var topic = (string?)config["messageTopic"] ?? "event";

var url = $"{mqEndpoint.TrimEnd('/')}/ws/v2/consumer/persistent/{accessId}/out/{topic}/{accessId}-sub?ackTimeoutMillis=3000&subscriptionType=Failover";
var key = Encoding.UTF8.GetBytes(accessSecret.Substring(8, 16));

while (true)
{
    using var ws = new ClientWebSocket();
    ws.Options.SetRequestHeader("username", accessId);
    ws.Options.SetRequestHeader("password", MqPassword(accessId, accessSecret));
    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
    try
    {
        await ws.ConnectAsync(new Uri(url), CancellationToken.None);
        Console.WriteLine($"{Now()}  Conectado a {mqEndpoint} (tópico {topic}). Ctrl+C para salir.");

        var buffer = new byte[64 * 1024];
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var message = JsonNode.Parse(ms.ToArray())!;
            Handle(message);

            var ack = Encoding.UTF8.GetBytes(new JsonObject { ["messageId"] = (string?)message["messageId"] }.ToJsonString());
            await ws.SendAsync(ack, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        Console.WriteLine($"{Now()}  Conexión cerrada: {ws.CloseStatus} {ws.CloseStatusDescription}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{Now()}  Error: {ex.Message}");
    }

    await Task.Delay(TimeSpan.FromSeconds(5));
}

void Handle(JsonNode message)
{
    try
    {
        var payload = JsonNode.Parse(Convert.FromBase64String((string)message["payload"]!))!;
        var protocol = (int?)payload["protocol"];
        var em = (string?)message["properties"]?["em"];
        var data = Decrypt((string)payload["data"]!, em == "aes_gcm");
        var body = JsonNode.Parse(data)!;

        if (protocol == 4)
        {
            foreach (var s in body["status"]!.AsArray())
            {
                var t = (long?)s!["t"];
                var delay = t is { } ms ? $"(llegó {(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (ms < 10_000_000_000 ? ms * 1000 : ms)) / 1000.0:N1} s después)" : "";
                Console.WriteLine($"{Now()}  {body["devId"]}  {s["code"]} = {s["value"]?.ToJsonString()}  {delay}");
            }
        }
        else
        {
            Console.WriteLine($"{Now()}  protocolo {protocol} ({(em ?? "ecb")}): {body.ToJsonString()}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{Now()}  Mensaje no decodificado ({ex.Message}): {message.ToJsonString()}");
    }
}

string Decrypt(string base64, bool gcm)
{
    var bytes = Convert.FromBase64String(base64);
    if (gcm)
    {
        var nonce = bytes[..12];
        var tag = bytes[^16..];
        var cipher = bytes[12..^16];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    using var ecb = Aes.Create();
    ecb.Key = key;
    return Encoding.UTF8.GetString(ecb.DecryptEcb(bytes, PaddingMode.PKCS7));
}

static string MqPassword(string id, string secret) => Md5Hex(id + Md5Hex(secret)).Substring(8, 16);

static string Md5Hex(string s) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(s)));

static string Now() => DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
