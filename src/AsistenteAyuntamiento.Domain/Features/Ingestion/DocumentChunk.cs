using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

/// <summary>A chunk of a flat-chunk RAG variant (baseline, recursive, semantic); each variant keeps its own rows.</summary>
public class DocumentChunk
{
    /// <summary>Strategy of the baseline variant, and of every row stored before the strategy was recorded.</summary>
    public const string FixedOverlapStrategy = "FixedOverlap";

    [Key]
    public int Id { get; set; }

    public string DocumentId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public DateTime PublicationDate { get; set; }

    /// <summary>Chunking strategy that produced the row (<c>FixedOverlap</c>, <c>Recursive</c>, <c>Semantic</c>).</summary>
    [MaxLength(32)]
    public string ChunkingStrategy { get; set; } = FixedOverlapStrategy;
}
