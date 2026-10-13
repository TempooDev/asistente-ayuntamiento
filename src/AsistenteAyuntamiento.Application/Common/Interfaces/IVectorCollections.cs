namespace AsistenteAyuntamiento.Application.Common.Interfaces;

/// <summary>Names of the vector collections, one per retrieval pipeline.</summary>
public static class VectorCollectionNames
{
    public const string DocumentChunks = "document_chunks";
    public const string ChildFragments = "child_fragments";
}

/// <summary>
/// Lifecycle of vector collections (port; Qdrant implementation in Infrastructure).
/// </summary>
public interface IVectorCollections
{
    /// <summary>
    /// Creates the collection (HNSW, cosine) if it does not exist. Throws if it exists with another vector size.
    /// Cheap after the first call per collection.
    /// </summary>
    Task EnsureAsync(string collection, int vectorSize, CancellationToken cancellationToken = default);

    /// <summary>Deletes the collection; it is recreated on the next write.</summary>
    Task DropAsync(string collection, CancellationToken cancellationToken = default);
}
