using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Domain.Features.Chat;

public class AiCallLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(100)]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ModelId { get; set; } = string.Empty;

    public bool Success { get; set; }

    public double DurationMs { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int TotalTokens { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Pipeline that made the call (<c>baseline</c>, <c>hierarchical</c>, <c>profile</c>…); null for rows written before it was recorded.</summary>
    [MaxLength(50)]
    public string? Pipeline { get; set; }

    /// <summary>What the call was for (<c>answer</c>, <c>query_expansion</c>, <c>history_analysis</c>); only answers count as calls in the usage summary.</summary>
    [Required]
    [MaxLength(50)]
    public string Operation { get; set; } = "answer";

    /// <summary>Provider of the model that answered (fallback included), to price the call; null for older rows.</summary>
    [MaxLength(50)]
    public string? Provider { get; set; }

    /// <summary>Streamed calls only: milliseconds until the first chunk; null for non-streamed calls and older rows.</summary>
    public double? TimeToFirstTokenMs { get; set; }
}
