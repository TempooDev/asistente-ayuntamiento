namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

public interface IChunkingStrategy
{
    IEnumerable<string> ChunkText(string text);
}
