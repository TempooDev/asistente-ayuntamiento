using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Domain.Features.Arena;

public class ArenaBattle
{
    [Key]
    public long Id { get; set; }

    public Guid SessionId { get; set; } = Guid.NewGuid();

    [Required]
    public string UserQuery { get; set; } = string.Empty;

    [Required]
    [MaxLength(30)]
    public string LeftSystem { get; set; } = string.Empty; // 'BASELINE_6000' or 'NUEVO_HIBRIDO'

    [Required]
    [MaxLength(30)]
    public string RightSystem { get; set; } = string.Empty;

    [Required]
    public string LeftResponse { get; set; } = string.Empty;

    [Required]
    public string RightResponse { get; set; } = string.Empty;

    public int LeftLatencyMs { get; set; }

    public int RightLatencyMs { get; set; }

    [Required]
    [MaxLength(20)]
    public string Winner { get; set; } = string.Empty; // 'LEFT', 'RIGHT', 'TIE', 'BOTH_BAD'

    [MaxLength(20)]
    public string? ClarityReason { get; set; } // 'LEFT', 'RIGHT', 'EQUAL'

    [MaxLength(20)]
    public string? PrecisionReason { get; set; } // 'LEFT', 'RIGHT', 'EQUAL'

    public string? OptionalComment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
