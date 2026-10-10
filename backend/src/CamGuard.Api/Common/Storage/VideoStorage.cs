using System.Globalization;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace CamGuard.Api.Common.Storage;

public class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Endpoint S3-compatible, p. ej. https://&lt;account&gt;.r2.cloudflarestorage.com</summary>
    public string Endpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "camguard";
    public int PresignedUrlMinutes { get; set; } = 10;
}

public interface IVideoStorage
{
    Task UploadAsync(string key, string filePath, string contentType, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    string GetPresignedUrl(string key, TimeSpan validFor, string? downloadFileName = null);
}

/// <summary>Claves de objeto: {cámara}/{yyyy}/{MM}/{dd}/{id}.mp4 y .jpg</summary>
public static class StorageKeys
{
    public static string Clip(int cameraId, Guid recordingId, DateTimeOffset startedAt) =>
        $"{Prefix(cameraId, startedAt)}/{recordingId:N}.mp4";

    public static string Thumbnail(int cameraId, Guid recordingId, DateTimeOffset startedAt) =>
        $"{Prefix(cameraId, startedAt)}/{recordingId:N}.jpg";

    // Cultura invariante: en un formato de fecha '/' es el separador de la cultura actual.
    private static string Prefix(int cameraId, DateTimeOffset startedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"{cameraId}/{startedAt.UtcDateTime:yyyy/MM/dd}");
}

/// <summary>Cloudflare R2 (o cualquier almacenamiento S3-compatible).</summary>
public sealed class S3VideoStorage : IVideoStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly bool _https;

    public S3VideoStorage(IOptions<StorageOptions> options)
    {
        var o = options.Value;
        _bucket = o.Bucket;
        _https = o.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        _client = new AmazonS3Client(
            new BasicAWSCredentials(o.AccessKey, o.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = o.Endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
                // R2 no acepta todos los checksums que el SDK agrega por defecto.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });
    }

    public async Task UploadAsync(string key, string filePath, string contentType, CancellationToken ct)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            FilePath = filePath,
            ContentType = contentType,
            // R2 no admite la firma por bloques (streaming SigV4); el SDK solo permite omitirla sobre HTTPS.
            DisablePayloadSigning = _https
        }, ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await _client.DeleteObjectAsync(_bucket, key, ct);
    }

    public string GetPresignedUrl(string key, TimeSpan validFor, string? downloadFileName = null)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(validFor),
            // El SDK firma con https por defecto aunque el endpoint sea http.
            Protocol = _https ? Protocol.HTTPS : Protocol.HTTP
        };

        if (downloadFileName is not null)
        {
            request.ResponseHeaderOverrides.ContentDisposition = $"attachment; filename=\"{downloadFileName}\"";
        }

        return _client.GetPreSignedURL(request);
    }

    public void Dispose() => _client.Dispose();
}
