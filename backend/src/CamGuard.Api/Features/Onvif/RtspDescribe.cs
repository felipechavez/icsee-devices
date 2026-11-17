using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CamGuard.Api.Features.Onvif;

/// <summary>
/// DESCRIBE RTSP para saber el códec real del video. ONVIF Media 1 no tiene valor para H.265 y varias cámaras
/// lo informan como H264 (o no lo informan): el SDP del propio stream es la fuente confiable.
/// </summary>
public static partial class RtspDescribe
{
    /// <summary>"H264", "H265" u otro códec de video del SDP (el primero); null si no hay video.</summary>
    public static string? VideoCodec(string sdp)
    {
        var inVideo = false;
        foreach (var raw in sdp.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("m=", StringComparison.Ordinal)) inVideo = line.StartsWith("m=video", StringComparison.Ordinal);
            if (!inVideo || RtpMap().Match(line) is not { Success: true } match) continue;
            var codec = match.Groups[1].Value.ToUpperInvariant();
            return codec == "HEVC" ? "H265" : codec;
        }
        return null;
    }

    /// <summary>Cabecera Authorization para un 401 con Digest (MD5, sin qop) o Basic.</summary>
    public static string? Authorization(string challenge, string method, string uri, string username, string password)
    {
        if (challenge.StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
            return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        if (!challenge.StartsWith("Digest", StringComparison.OrdinalIgnoreCase)) return null;
        var realm = Param(challenge, "realm");
        var nonce = Param(challenge, "nonce");
        if (realm is null || nonce is null) return null;

        var ha1 = Md5Hex($"{username}:{realm}:{password}");
        var ha2 = Md5Hex($"{method}:{uri}");
        var response = Md5Hex($"{ha1}:{nonce}:{ha2}");
        return $"Digest username=\"{username}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{uri}\", response=\"{response}\"";
    }

    /// <summary>Hace DESCRIBE (con un reintento autenticado) y devuelve el SDP.</summary>
    /// <param name="uri">URI RTSP sin credenciales (las de la cámara van en la cabecera).</param>
    public static async Task<string> FetchSdpAsync(string uri, string username, string password, TimeSpan timeout, CancellationToken ct)
    {
        var parsed = new Uri(uri);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var client = new TcpClient();
        await client.ConnectAsync(parsed.Host.Trim('[', ']'), parsed.IsDefaultPort || parsed.Port <= 0 ? 554 : parsed.Port, cts.Token);
        await using var stream = client.GetStream();

        var first = await RequestAsync(stream, uri, 1, null, cts.Token);
        var response = first;
        if (first.Code == 401)
        {
            var challenge = first.Headers.GetValueOrDefault("WWW-Authenticate")
                            ?? throw new OnvifException("El RTSP pidió autenticación sin indicar cuál.");
            var auth = Authorization(challenge, "DESCRIBE", uri, username, password)
                       ?? throw new OnvifException("El RTSP pide una autenticación que no se admite.");
            response = await RequestAsync(stream, uri, 2, auth, cts.Token);
        }

        return response.Code switch
        {
            200 => response.Body,
            401 => throw new OnvifException("El RTSP rechazó usuario o contraseña.", unauthorized: true),
            _ => throw new OnvifException($"El RTSP respondió {response.Code.ToString(CultureInfo.InvariantCulture)}.")
        };
    }

    private record RtspResponse(int Code, Dictionary<string, string> Headers, string Body);

    private static async Task<RtspResponse> RequestAsync(NetworkStream stream, string uri, int cseq, string? authorization, CancellationToken ct)
    {
        var request = new StringBuilder()
            .Append("DESCRIBE ").Append(uri).Append(" RTSP/1.0\r\n")
            .Append("CSeq: ").Append(cseq.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("User-Agent: CamGuard\r\nAccept: application/sdp\r\n");
        if (authorization is not null) request.Append("Authorization: ").Append(authorization).Append("\r\n");
        request.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request.ToString()), ct);

        // Encabezados hasta la línea vacía y luego Content-Length bytes de cuerpo (tope de 64 KB).
        var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int headerEnd;
        while ((headerEnd = IndexOfHeaderEnd(buffer)) < 0)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0 || buffer.Length > 64 * 1024) throw new OnvifException("El RTSP cerró la conexión sin responder.");
            buffer.Write(chunk, 0, read);
        }

        var all = buffer.ToArray();
        var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var code = lines[0].Split(' ') is [_, var status, ..] && int.TryParse(status, CultureInfo.InvariantCulture, out var c) ? c : 0;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
        }

        var length = headers.TryGetValue("Content-Length", out var l) && int.TryParse(l, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, 0, 64 * 1024)
            : 0;
        var body = new MemoryStream();
        body.Write(all, headerEnd + 4, all.Length - headerEnd - 4);
        while (body.Length < length)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) break;
            body.Write(chunk, 0, read);
        }

        return new RtspResponse(code, headers, Encoding.UTF8.GetString(body.ToArray(), 0, (int)Math.Min(body.Length, length)));
    }

    private static int IndexOfHeaderEnd(MemoryStream buffer)
    {
        var data = buffer.GetBuffer();
        for (var i = 0; i + 3 < buffer.Length; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n') return i;
        }
        return -1;
    }

    private static string? Param(string challenge, string name)
    {
        var match = Regex.Match(challenge, $"{name}=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Md5Hex(string text) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex(@"^a=rtpmap:\d+\s+([A-Za-z0-9\-]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex RtpMap();
}
