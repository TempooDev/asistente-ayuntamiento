namespace AsistenteAyuntamiento.Application.Features.Chat.DTOs;

using System;

public record ArenaChatVoteRequest(Guid ChatSessionId, Guid BattleId, string Winner);
