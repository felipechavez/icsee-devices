using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Common.Notifications;

public class NtfyOptions
{
    public const string Section = "Ntfy";

    /// <summary>URL completa del tópico, p. ej. https://ntfy.midominio.com/camguard. Vacía = desactivado.</summary>
    public string Url { get; set; } = "";

    /// <summary>Token de acceso opcional (instancias con autenticación).</summary>
    public string? Token { get; set; }
}

public record Notification(string Title, string Message, string? ClickUrl = null, string? ImageUrl = null, string[]? Tags = null);

public interface INotifier
{
    Task NotifyAsync(Notification notification, CancellationToken ct);
}

/// <summary>
/// Publica en ntfy usando el formato JSON (permite tildes en el título,
/// cosa que las cabeceras HTTP no admiten).
/// </summary>
public class NtfyNotifier(HttpClient http, IOptions<NtfyOptions> options, ILogger<NtfyNotifier> logger) : INotifier
{
    public async Task NotifyAsync(Notification notification, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Url))
        {
            logger.LogDebug("ntfy desactivado; no se envía la notificación");
            return;
        }

        var topicUri = new Uri(o.Url);
        var topic = topicUri.Segments[^1].Trim('/');
        var baseUri = new Uri(topicUri, "/");

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUri)
        {
            Content = JsonContent.Create(new
            {
                topic,
                title = notification.Title,
                message = notification.Message,
                click = notification.ClickUrl,
                attach = notification.ImageUrl,
                tags = notification.Tags
            })
        };

        if (!string.IsNullOrWhiteSpace(o.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.Token);
        }

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
