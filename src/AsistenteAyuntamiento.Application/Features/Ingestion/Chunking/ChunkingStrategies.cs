using AsistenteAyuntamiento.Domain.Features.Ingestion;

namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

/// <summary>Keys of the registered strategies; the same values are stored in <c>DocumentChunks.ChunkingStrategy</c>.</summary>
public static class ChunkingStrategies
{
    public const string FixedOverlap = DocumentChunk.FixedOverlapStrategy;
    public const string Recursive = "Recursive";
    public const string Semantic = "Semantic";
}
