namespace CamGuard.Api.Features.Onvif;

public class OnvifOptions
{
    public const string Section = "Onvif";

    /// <summary>Segundos sin movimiento (tras el "State=false" de la cámara) antes de cortar el clip.</summary>
    public int AsleepDelaySeconds { get; set; } = 10;

    /// <summary>Cuánto espera la cámara eventos en cada PullMessages antes de responder vacío.</summary>
    public int PullTimeoutSeconds { get; set; } = 10;

    /// <summary>Vigencia de la suscripción PullPoint; se renueva a la mitad.</summary>
    public int SubscriptionSeconds { get; set; } = 120;

    /// <summary>Tiempo máximo de cada petición HTTP a la cámara (además del tiempo de PullMessages).</summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>Cada cuánto se revisan las cámaras ONVIF en BD (altas, bajas y cambios de datos).</summary>
    public int CameraRefreshSeconds { get; set; } = 15;
}
