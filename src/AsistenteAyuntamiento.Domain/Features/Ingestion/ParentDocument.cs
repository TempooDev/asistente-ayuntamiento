using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

public class ParentDocument
{
    [Key]
    public long Id { get; set; }

    [Required]
    [MaxLength(10)]
    public string Bulletin { get; set; } = string.Empty; // 'BOE', 'BOJA'

    [Required]
    [MaxLength(50)]
    public string DocumentId { get; set; } = string.Empty; // e.g., 'BOE-A-2024-1234'

    [MaxLength(50)]
    public string? NormativeRank { get; set; } // 'Ley', 'Real Decreto', 'Orden'

    public string? IssuingBody { get; set; } // Ministerio, Consejería

    [Required]
    public string NormTitle { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? NormSection { get; set; } // 'Artículo 12', 'Disposición Adicional 1'

    [MaxLength(100)]
    public string? Municipality { get; set; } // NULL for state/regional norms

    [Required]
    public string FullText { get; set; } = string.Empty; // Full article text

    public DateTime PublicationDate { get; set; }

    public bool IsActive { get; set; } = true;

    public string Metadata { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ChildFragment> Children { get; set; } = new List<ChildFragment>();
}
