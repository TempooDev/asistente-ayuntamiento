using System.ComponentModel.DataAnnotations;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

public class ChildFragment
{
    [Key]
    public long Id { get; set; }

    public long ParentId { get; set; }

    public BulletinType Bulletin { get; set; }

    [MaxLength(100)]
    public string? Municipality { get; set; }

    [MaxLength(100)]
    public string? SubSection { get; set; } // 'Apartado 1', 'Párrafo 2'

    [Required]
    public string ChunkText { get; set; } = string.Empty; // breadcrumb + questions + body

    // Navigation
    public ParentDocument Parent { get; set; } = null!;
}
