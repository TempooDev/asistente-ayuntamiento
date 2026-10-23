using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Evaluation;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Evaluation;

/// <summary>Stored offline evaluation runs (spec: evaluation-results). Platform data: no tenant filter applies.</summary>
public interface IEvaluationResultsService
{
    /// <summary>Newest first, each with the scores the history chart needs.</summary>
    Task<IReadOnlyList<EvaluationRunSummaryDto>> ListRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>The run with every pipeline result, or null when it was never imported.</summary>
    Task<EvaluationRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default);
}

public sealed class EvaluationResultsService(IAppDbContext dbContext) : IEvaluationResultsService
{
    public async Task<IReadOnlyList<EvaluationRunSummaryDto>> ListRunsAsync(CancellationToken cancellationToken = default)
    {
        var runs = await dbContext.EvaluationRuns.AsNoTracking().Include(r => r.Results)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync(cancellationToken);

        return [.. runs.Select(r => new EvaluationRunSummaryDto(r.RunId, r.StartedAt, r.FinishedAt, r.ChatModel, r.EmbeddingModel, r.JudgeModel,
            r.QuestionCount, r.ImportedAt, [.. r.Results.OrderBy(p => p.Pipeline).Select(p => new PipelineScoreDto(p.Pipeline, p.Faithfulness, p.ContextRecall))]))];
    }

    public async Task<EvaluationRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var run = await dbContext.EvaluationRuns.AsNoTracking().Include(r => r.Results)
            .FirstOrDefaultAsync(r => r.RunId == runId, cancellationToken);

        return run is null ? null : new EvaluationRunDto(run.RunId, run.StartedAt, run.FinishedAt, run.GitCommit, run.Seed, run.ChatModel,
            run.EmbeddingModel, run.JudgeModel, run.QueryInstruction, run.QuestionCount, run.DatasetHash, run.CorpusHash, run.RagasVersion,
            run.ImportedAt, [.. run.Results.OrderBy(p => p.Pipeline)]);
    }
}

public sealed record PipelineScoreDto(string Pipeline, double? Faithfulness, double? ContextRecall);

public sealed record EvaluationRunSummaryDto(string RunId, DateTime StartedAt, DateTime? FinishedAt, string ChatModel, string EmbeddingModel,
    string JudgeModel, int QuestionCount, DateTime ImportedAt, IReadOnlyList<PipelineScoreDto> Results);

public sealed record EvaluationRunDto(string RunId, DateTime StartedAt, DateTime? FinishedAt, string? GitCommit, int Seed, string ChatModel,
    string EmbeddingModel, string JudgeModel, string? QueryInstruction, int QuestionCount, string DatasetHash, string CorpusHash,
    string? RagasVersion, DateTime ImportedAt, IReadOnlyList<EvaluationPipelineResult> Results);
