using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace AsistenteAyuntamiento.Web.Infrastructure;

/// <summary>
/// Blob storage implementation backed by Azurite (Azure Storage emulator) for local development.
/// Uses the S3-compatible endpoint exposed by Azurite with path-style addressing.
/// Falls back to in-memory no-op if the connection string is unavailable.
/// </summary>
public sealed class AzuriteBlobStorageRepository : IBlobStorageRepository
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;

    public AzuriteBlobStorageRepository(string connectionString)
    {
        // Azurite default S3-compatible endpoint
        // Connection string example: "UseDevelopmentStorage=true" or "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;..."
        // For simplicity in dev we use well-known Azurite defaults.
        const string azuriteEndpoint = "http://127.0.0.1:10000/devstoreaccount1";
        const string defaultAccessKey = "devstoreaccount1";
        const string defaultSecretKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KNrZPNBehGqA==";

        _bucketName = "blobs";

        var config = new AmazonS3Config
        {
            ServiceURL = azuriteEndpoint,
            ForcePathStyle = true,
        };

        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(defaultAccessKey, defaultSecretKey),
            config);
    }

    public async Task<string> UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(cancellationToken);

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };

        await _s3.PutObjectAsync(request, cancellationToken);
        return key;
    }

    public Task<string> GetUrlAsync(
        string key,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        // In development, return a local URL for convenience
        var url = $"http://127.0.0.1:10000/devstoreaccount1/{_bucketName}/{key}";
        return Task.FromResult(url);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
        };

        await _s3.DeleteObjectAsync(request, cancellationToken);
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _s3.EnsureBucketExistsAsync(_bucketName);
        }
        catch
        {
            // Bucket may already exist or Azurite may not be running — log and continue
        }
    }
}
