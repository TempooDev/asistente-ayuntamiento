namespace AsistenteAyuntamiento.Application.Common.VectorStore;

/// <summary>Logical vector collections, one per retrieval pipeline. Physical names live in Infrastructure options.</summary>
public enum VectorCollection
{
    /// <summary>Baseline pipeline: one point per <c>DocumentChunk</c> of the fixed strategy.</summary>
    DocumentChunks,

    /// <summary>Hierarchical pipeline: one point per <c>ChildFragment</c>.</summary>
    ChildFragments,

    /// <summary>Recursive flat-chunk variant: one point per <c>DocumentChunk</c> of the recursive strategy.</summary>
    RecursiveChunks,

    /// <summary>Semantic flat-chunk variant: one point per <c>DocumentChunk</c> of the semantic strategy.</summary>
    SemanticChunks
}
