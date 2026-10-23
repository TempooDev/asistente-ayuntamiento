using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Domain.Features.Evaluation;

/// <summary>
/// One pipeline's figures in an <see cref="EvaluationRun"/>, from the scorer's summary, latency and indexing reports.
/// Scores and rates go from 0 to 1; a metric that could not be computed is null, never 0.
/// </summary>
public class EvaluationPipelineResult
{
    [MaxLength(50)]
    public string RunId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Pipeline { get; set; } = string.Empty;

    public double? ContextPrecision { get; set; }
    public double? ContextPrecisionStd { get; set; }
    public double? ContextRecall { get; set; }
    public double? ContextRecallStd { get; set; }
    public double? Faithfulness { get; set; }
    public double? FaithfulnessStd { get; set; }
    public double? AnswerRelevancy { get; set; }
    public double? AnswerRelevancyStd { get; set; }

    public double? HallucinationRate { get; set; }
    public double? HitAt1 { get; set; }
    public double? HitAt3 { get; set; }
    public double? HitAt5 { get; set; }
    public double? Mrr { get; set; }
    public double? NoAnswerDetectedRate { get; set; }

    public int Failed { get; set; }
    public int Missing { get; set; }

    public double? LatencyP50Ms { get; set; }
    public double? LatencyP95Ms { get; set; }
    public double? TtftP50Ms { get; set; }
    public double? TtftP95Ms { get; set; }
    public double? MeanTokens { get; set; }

    public double? IndexingP50Ms { get; set; }
    public double? IndexingP95Ms { get; set; }
    public double? ChunksPerDocument { get; set; }
    public long? Points { get; set; }
}
