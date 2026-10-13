namespace AsistenteAyuntamiento.Application.Features.Chat.DTOs;

/// <summary>
/// Chunk of an Arena stream. <see cref="ErrorCode"/> is only set on the last chunk of a side that failed
/// (<c>INTERNAL</c> or an <c>LLM_*</c> code); its <see cref="Content"/> is then empty.
/// </summary>
public record ArenaStreamChunk(string Option, string Content)
{
    public string? ErrorCode { get; init; }
}
