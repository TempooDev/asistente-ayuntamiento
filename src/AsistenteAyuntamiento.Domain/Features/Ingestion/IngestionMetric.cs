using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

public class IngestionMetric
{
    [Key]
    public long Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Pipeline { get; set; } = string.Empty; // 'BASELINE_FLAT' or 'HIERARCHICAL'

    [Required]
    [MaxLength(10)]
    public string Bulletin { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string DocumentId { get; set; } = string.Empty;

    public int TotalTokensEmbedded { get; set; }

    public int TotalLlmCalls { get; set; }

    public int TotalLlmTokens { get; set; }

    public long ProcessingDurationMs { get; set; }

    public int ChunksGenerated { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
