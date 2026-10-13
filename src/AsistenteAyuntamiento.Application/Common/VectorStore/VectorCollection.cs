namespace AsistenteAyuntamiento.Application.Common.VectorStore;

/// <summary>Logical vector collections, one per retrieval pipeline. Physical names live in Infrastructure options.</summary>
public enum VectorCollection
{
    /// <summary>Baseline pipeline: one point per <c>DocumentChunk</c>.</summary>
    DocumentChunks,

    /// <summary>Hierarchical pipeline: one point per <c>ChildFragment</c>.</summary>
    ChildFragments
}
