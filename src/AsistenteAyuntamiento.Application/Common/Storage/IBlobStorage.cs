namespace AsistenteAyuntamiento.Application.Common.Storage;

/// <summary>
/// Object storage port for scraped documents (S3/MinIO implementation in Infrastructure).
/// Keys are relative to the configured bucket, e.g. <c>json/BOE/BOE-A-2026-1.json</c>.
/// </summary>
public interface IBlobStorage
{
    /// <summary>Creates the bucket if it does not exist (startup convenience; failures are logged, not thrown).</summary>
    Task EnsureContainerAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists every object under <paramref name="prefix"/>, following pagination.</summary>
    IAsyncEnumerable<BlobObjectInfo> ListAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Opens an object for reading. The caller disposes the stream.</summary>
    /// <exception cref="BlobNotFoundException">The object does not exist.</exception>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
