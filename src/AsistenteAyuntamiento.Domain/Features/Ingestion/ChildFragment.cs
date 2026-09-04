using System.ComponentModel.DataAnnotations;
using Pgvector;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

public class ChildFragment
{
    [Key]
    public long Id { get; set; }

    public long ParentId { get; set; }

    [Required]
    [MaxLength(10)]
    public string Bulletin { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Municipality { get; set; }

    [MaxLength(100)]
    public string? SubSection { get; set; } // 'Apartado 1', 'Párrafo 2'

    [Required]
    public string ChunkText { get; set; } = string.Empty; // breadcrumb + questions + body

    // TsvContent is managed by a DB trigger, not set from C#
    public string? TsvContent { get; set; }

    public Vector? Embedding { get; set; }

    // Navigation
    public ParentDocument Parent { get; set; } = null!;
}
