using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Domain.Features.Evaluation;

/// <summary>
/// A scored offline evaluation run (<c>eval/runs/{RunId}</c>), imported by the evaluation runner. Platform-wide data:
/// the corpus and the pipelines are shared, so there is no tenant.
/// </summary>
public class EvaluationRun
{
    [Key]
    [MaxLength(50)]
    public string RunId { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    [MaxLength(64)]
    public string? GitCommit { get; set; }

    public int Seed { get; set; }

    [MaxLength(100)]
    public string ChatModel { get; set; } = string.Empty;

    [MaxLength(100)]
    public string EmbeddingModel { get; set; } = string.Empty;

    [MaxLength(100)]
    public string JudgeModel { get; set; } = string.Empty;

    public string? QueryInstruction { get; set; }

    public int QuestionCount { get; set; }

    [MaxLength(100)]
    public string DatasetHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string CorpusHash { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? RagasVersion { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public List<EvaluationPipelineResult> Results { get; set; } = [];
}
