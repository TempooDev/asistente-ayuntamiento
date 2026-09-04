using System.ComponentModel.DataAnnotations;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

public class IngestionMetric
{
    [Key]
    public long Id { get; set; }

    public PipelineType Pipeline { get; set; }

    public BulletinType Bulletin { get; set; }

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
