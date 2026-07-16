using Amazon.S3;
using Amazon.S3.Model;

namespace AsistenteAyuntamiento.Web.Infrastructure;

/// <summary>
/// Blob storage implementation backed by an S3-compatible store.
/// In production: Cloudflare R2 (ServiceURL = https://&lt;accountId&gt;.r2.cloudflarestorage.com).
/// In local dev: Can also point to MinIO or similar if needed.
/// </summary>
public sealed class S3BlobStorageRepository(IAmazonS3 s3, string bucketName) : IBlobStorageRepository
{
    public async Task<string> UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };

        await s3.PutObjectAsync(request, cancellationToken);
        return key;
    }

    public async Task<string> GetUrlAsync(
        string key,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry ?? TimeSpan.FromHours(1)),
            Protocol = Protocol.HTTPS,
        };

        return await Task.FromResult(s3.GetPreSignedURL(request));
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = key,
        };

        await s3.DeleteObjectAsync(request, cancellationToken);
    }
}
