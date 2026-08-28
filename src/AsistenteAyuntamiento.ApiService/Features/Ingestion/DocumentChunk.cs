using Pgvector;
using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.ApiService.Features.Ingestion;

public class DocumentChunk
{
    [Key]
    public int Id { get; set; }
    
    public string DocumentId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public DateTime PublicationDate { get; set; }
    
    // Nomic Embed Text model produces 768 dimensions. Llama3.2 produces variable depending on version. We'll use a dynamic column if possible, but pgvector requires dimension constraint typically, e.g. vector(768) or vector(384). We will define it via FluentAPI.
    public Vector? Embedding { get; set; }
}
