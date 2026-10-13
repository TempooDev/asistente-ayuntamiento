using AsistenteAyuntamiento.Application.Features.Chat.DTOs;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>Arena mode: both pipelines answer the same turn side by side (anonymised as Alfa and Beta).</summary>
public interface IArenaStreamingService
{
    /// <summary>
    /// First yields the battle id (<c>Option = "SessionId"</c>), then chunks of either side as soon as they arrive.
    /// The battle is stored when both sides finish.
    /// </summary>
    IAsyncEnumerable<ArenaStreamChunk> StreamAsync(ChatTurn turn, CancellationToken cancellationToken = default);
}
