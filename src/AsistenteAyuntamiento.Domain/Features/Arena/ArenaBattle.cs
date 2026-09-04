using System.ComponentModel.DataAnnotations;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Domain.Features.Arena;

public class ArenaBattle
{
    [Key]
    public long Id { get; set; }

    public Guid SessionId { get; set; } = Guid.NewGuid();

    [Required]
    public string UserQuery { get; set; } = string.Empty;

    public PipelineType LeftSystem { get; set; }

    public PipelineType RightSystem { get; set; }

    [Required]
    public string LeftResponse { get; set; } = string.Empty;

    [Required]
    public string RightResponse { get; set; } = string.Empty;

    public int LeftLatencyMs { get; set; }

    public int RightLatencyMs { get; set; }

    public int LeftTokens { get; set; }

    public int RightTokens { get; set; }

    public BattleWinner Winner { get; set; } = BattleWinner.Pending;

    public EvaluationPreference? ClarityReason { get; set; }

    public EvaluationPreference? PrecisionReason { get; set; }

    public string? OptionalComment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
