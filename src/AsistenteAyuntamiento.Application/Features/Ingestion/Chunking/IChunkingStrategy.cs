namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

public interface IChunkingStrategy
{
    string StrategyName { get; }
    IEnumerable<DocumentChunkResult> ChunkText(string text);
}
