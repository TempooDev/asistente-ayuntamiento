using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Chat.Pipelines;

/// <summary>A retrieval-augmented answering strategy, compared against the others in the Arena.</summary>
public interface IRagPipeline
{
    PipelineType Type { get; }

    /// <summary>Streams the answer to <paramref name="turn"/>. The pipeline may rewrite the turn's history.</summary>
    IAsyncEnumerable<string> StreamAsync(ChatTurn turn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers one question for the offline evaluation: returns the answer with its retrieved contexts, generation
    /// context, tokens and stage timings. Stores no chat message, battle or shadow test, and never throws for model errors.
    /// </summary>
    Task<RagEvaluationResult> RunForEvaluationAsync(EvaluationTurn turn, CancellationToken cancellationToken = default);
}
