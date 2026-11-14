namespace CamGuard.Api.Features.Upload;

public class UploadOptions
{
    public const string Section = "Upload";

    /// <summary>
    /// Espacio libre mínimo en el disco de {DataDirectory}: por debajo se borran clips en espera.
    /// Menor que <c>Health:MinFreeDiskMb</c>, así primero llega el aviso y después se borra.
    /// </summary>
    public int MinFreeDiskMb { get; set; } = 512;
}
