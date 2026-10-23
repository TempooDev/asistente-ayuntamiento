using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat.Pipelines;

/// <summary>A single-turn question asked by the evaluation runner, outside any chat session.</summary>
/// <param name="TenantId">A tenant without its own AI configuration, so every pipeline answers with the platform model.</param>
public sealed record EvaluationTurn(string Question, string TenantId = EvaluationTurn.DefaultTenant, string UserId = EvaluationTurn.DefaultUser)
{
    public const string DefaultTenant = "evaluation";
    public const string DefaultUser = "evaluation-runner";

    internal ChatTurn ToChatTurn()
    {
        var history = new ChatHistory();
        history.AddUserMessage(Question);
        return new ChatTurn(history, TenantId, UserId) { TelemetryPipeline = LlmPipelines.Evaluation };
    }
}

/// <summary>A unit the search returned, in rank order: a chunk for flat variants, a child fragment for the hierarchical one.</summary>
public sealed record RetrievedContext(string DocumentId, double Score, string Text);

/// <summary>Milliseconds per stage. <see cref="ExpansionMs"/> is 0 for pipelines without query expansion.</summary>
public sealed record RagEvaluationTimings(double ExpansionMs, double RetrievalMs, double? TimeToFirstTokenMs, double GenerationMs, double TotalMs);

/// <summary>
/// Everything the RAGAS scorer needs about one answer. Model errors come back as <see cref="ErrorCode"/>
/// (<see cref="LlmErrorCodes"/>) with an empty answer, never as exceptions.
/// </summary>
/// <param name="GenerationContext">The document text given to the generation model.</param>
/// <param name="TokenUsage">Tokens of every LLM call of the run (query expansion included).</param>
public sealed record RagEvaluationResult(
    PipelineType Pipeline,
    string Answer,
    IReadOnlyList<RetrievedContext> Contexts,
    string GenerationContext,
    TokenUsageInfo TokenUsage,
    RagEvaluationTimings Timings,
    string? ErrorCode = null);
