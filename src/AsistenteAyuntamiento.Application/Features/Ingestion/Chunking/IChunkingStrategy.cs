namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

/// <summary>Splits a document's text into the units that are embedded and retrieved.</summary>
public interface IChunkingStrategy
{
    /// <summary>Name stored with each chunk (<c>DocumentChunks.ChunkingStrategy</c>) and used as the DI key.</summary>
    string StrategyName { get; }

    /// <summary>
    /// Chunks <paramref name="text"/>; empty or whitespace text gives no chunks. Asynchronous because a strategy may
    /// call a model (the semantic one embeds sentences); a failure is thrown, never replaced by another strategy.
    /// </summary>
    Task<IReadOnlyList<DocumentChunkResult>> ChunkTextAsync(string text, CancellationToken cancellationToken = default);
}
