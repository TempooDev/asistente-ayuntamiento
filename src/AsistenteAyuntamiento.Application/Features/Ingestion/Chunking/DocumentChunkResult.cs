namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

public class DocumentChunkResult
{
    public string Text { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
