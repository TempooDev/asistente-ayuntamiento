using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Chat.Pipelines;

/// <summary>A retrieval-augmented answering strategy, compared against the others in the Arena.</summary>
public interface IRagPipeline
{
    PipelineType Type { get; }

    /// <summary>Streams the answer to <paramref name="turn"/>. The pipeline may rewrite the turn's history.</summary>
    IAsyncEnumerable<string> StreamAsync(ChatTurn turn, CancellationToken cancellationToken = default);
}
