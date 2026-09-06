using Pgvector;
using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

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
}
