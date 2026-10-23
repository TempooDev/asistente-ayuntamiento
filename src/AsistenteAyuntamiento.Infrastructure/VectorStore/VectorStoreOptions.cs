using AsistenteAyuntamiento.Application.Common.VectorStore;

namespace AsistenteAyuntamiento.Infrastructure.VectorStore;

/// <summary>Physical collection names (configuration section <c>VectorStore</c>).</summary>
public sealed class VectorStoreOptions
{
    public const string SectionName = "VectorStore";

    public string DocumentChunksCollection { get; set; } = "document_chunks";

    public string ChildFragmentsCollection { get; set; } = "child_fragments";

    public string RecursiveChunksCollection { get; set; } = "document_chunks_recursive";

    public string SemanticChunksCollection { get; set; } = "document_chunks_semantic";

    public string NameOf(VectorCollection collection) => collection switch
    {
        VectorCollection.DocumentChunks => DocumentChunksCollection,
        VectorCollection.ChildFragments => ChildFragmentsCollection,
        VectorCollection.RecursiveChunks => RecursiveChunksCollection,
        VectorCollection.SemanticChunks => SemanticChunksCollection,
        _ => throw new ArgumentOutOfRangeException(nameof(collection), collection, null)
    };
}
