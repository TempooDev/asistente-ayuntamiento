namespace AsistenteAyuntamiento.Application.Features.Chat.DTOs;

using System;

public record ChatSessionSummaryDto(Guid Id, DateTime CreatedAt, string Preview, int MessageCount);
public record ChatMessageDto(string Role, string Content, DateTime CreatedAt);
public record ArenaStreamChunk(string Option, string Content);
public record ArenaChatVoteRequest(Guid ChatSessionId, Guid BattleId, string Winner);
