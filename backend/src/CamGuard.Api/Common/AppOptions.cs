using System.Net.Http.Headers;
using System.Text;

namespace CamGuard.Api.Common;

public class AppOptions
{
    public const string Section = "App";

    /// <summary>Carpeta local para clips en curso y la cola de subida.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>URL pública de la web, usada en los enlaces de las notificaciones.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";
}

public class Go2rtcOptions
{
    public const string Section = "Go2rtc";

    /// <summary>API HTTP de go2rtc (señalización WebRTC).</summary>
    public string BaseUrl { get; set; } = "http://localhost:1984";

    /// <summary>Usuario y contraseña de la API de go2rtc (sección api: de go2rtc.yaml). Opcional.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>
    /// Servidor RTSP de go2rtc; el stream de cada cámara se agrega al final.
    /// Si go2rtc pide credenciales RTSP van en la URL: rtsp://user:pass@host:8554
    /// </summary>
    public string RtspBaseUrl { get; set; } = "rtsp://localhost:8554";

    public string RtspUrlFor(string stream) => $"{RtspBaseUrl.TrimEnd('/')}/{stream}";

    /// <summary>Dirección base y autenticación básica para los clientes HTTP de la API de go2rtc.</summary>
    public void ConfigureHttpClient(HttpClient client)
    {
        client.BaseAddress = new Uri(BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrEmpty(Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }
}
