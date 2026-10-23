using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>One line of <c>answers.jsonl</c>: a question answered by one pipeline.</summary>
/// <param name="Position">Index of the question in the run; position 0 is each pipeline's warm-up (excluded from latency).</param>
public sealed record AnswerLine(
    string QuestionId,
    PipelineType Pipeline,
    int Position,
    bool Warmup,
    string Question,
    string Answer,
    IReadOnlyList<ContextLine> Contexts,
    string GenerationContext,
    int InputTokens,
    int OutputTokens,
    TimingsLine Timings,
    string? ErrorCode)
{
    public static AnswerLine From(EvaluationQuestion question, int position, RagEvaluationResult result) => new(
        question.Id,
        result.Pipeline,
        position,
        position == 0,
        question.Question,
        result.Answer,
        result.Contexts.Select(c => new ContextLine(c.DocumentId, c.Score, c.Text)).ToList(),
        result.GenerationContext,
        result.TokenUsage.InputTokens,
        result.TokenUsage.OutputTokens,
        new TimingsLine(result.Timings.ExpansionMs, result.Timings.RetrievalMs, result.Timings.TimeToFirstTokenMs, result.Timings.GenerationMs, result.Timings.TotalMs),
        result.ErrorCode);
}

public sealed record ContextLine(string DocumentId, double Score, string Text);

public sealed record TimingsLine(double ExpansionMs, double RetrievalMs, double? TtftMs, double GenerationMs, double TotalMs);

/// <summary><c>run.json</c>: what a run used, so its numbers can be reproduced and compared.</summary>
public sealed record RunInfo(
    string RunId,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? GitCommit,
    int Seed,
    string ChatProvider,
    string ChatModel,
    double Temperature,
    string EmbeddingModel,
    string? QueryInstruction,
    IReadOnlyList<PipelineType> Pipelines,
    int QuestionCount,
    string DatasetHash,
    string CorpusHash,
    string? OllamaEndpoint = null,
    string? Hardware = null);

/// <summary><c>indexing.json</c>: ingestion cost per pipeline for the corpus documents (latest metric per document).</summary>
public sealed record IndexingReport(IReadOnlyList<PipelineIndexing> Pipelines);

public sealed record PipelineIndexing(PipelineType Pipeline, string Collection, long Points, IReadOnlyList<DocumentIndexing> Documents);

public sealed record DocumentIndexing(string DocumentId, long ProcessingMs, int Chunks, int EmbeddedTokens, int LlmCalls, int LlmTokens);
