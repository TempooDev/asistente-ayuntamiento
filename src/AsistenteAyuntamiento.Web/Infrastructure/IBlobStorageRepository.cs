namespace AsistenteAyuntamiento.Web.Infrastructure;

/// <summary>
/// Abstraction for blob (object) storage operations.
/// Implemented by S3BlobStorageRepository (Cloudflare R2) and AzuriteBlobStorageRepository (dev).
/// </summary>
public interface IBlobStorageRepository
{
    /// <summary>Upload a blob. The key is the full object path (e.g. "documents/file.pdf").</summary>
    Task<string> UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Returns a pre-signed URL valid for the given duration, or a public URL if the bucket is public.</summary>
    Task<string> GetUrlAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes a blob by key.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
