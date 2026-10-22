namespace AsistenteAyuntamiento.Application.Common.VectorStore;

/// <summary>
/// Vector database port (Qdrant implementation in Infrastructure).
/// Collections are created on first write with the embedding size (HNSW, cosine).
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Creates the collection if it does not exist. Throws if it exists with another vector size.
    /// Cheap after the first call per collection.
    /// </summary>
    Task EnsureCollectionAsync(VectorCollection collection, int vectorSize, CancellationToken cancellationToken = default);

    Task<bool> CollectionExistsAsync(VectorCollection collection, CancellationToken cancellationToken = default);

    /// <summary>Deletes the collection; it is recreated on the next write.</summary>
    Task DropCollectionAsync(VectorCollection collection, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces points by id.</summary>
    Task UpsertAsync(VectorCollection collection, IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every point of a document: those whose <see cref="VectorPayloadKeys.DocumentId"/> matches, plus
    /// points that only carry one of <paramref name="parentIds"/> (payloads written before DocumentId existed).
    /// </summary>
    Task DeleteDocumentAsync(VectorCollection collection, string documentId, IReadOnlyCollection<long>? parentIds = null, CancellationToken cancellationToken = default);

    /// <summary>Vector size and number of points, or null when the collection does not exist.</summary>
    Task<VectorCollectionStats?> GetStatsAsync(VectorCollection collection, CancellationToken cancellationToken = default);

    /// <summary>Number of points whose <see cref="VectorPayloadKeys.DocumentId"/> is <paramref name="documentId"/>.</summary>
    Task<long> CountDocumentPointsAsync(VectorCollection collection, string documentId, CancellationToken cancellationToken = default);

    /// <summary>Returns the nearest points, best first.</summary>
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorCollection collection, VectorSearchRequest request, CancellationToken cancellationToken = default);
}
