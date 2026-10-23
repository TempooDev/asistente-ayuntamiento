namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

public class DocumentChunkResult
{
    public string Text { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Tokens the strategy embedded to place this document's boundaries (semantic strategy). Set on the first chunk
    /// only, so summing it over the chunks gives the document's total; <c>null</c> when the strategy embeds nothing.
    /// </summary>
    public int? EmbeddedTokens { get; set; }
}
