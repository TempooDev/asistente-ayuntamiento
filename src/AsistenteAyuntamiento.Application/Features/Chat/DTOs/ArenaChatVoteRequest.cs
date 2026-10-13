using System;

namespace AsistenteAyuntamiento.Application.Features.Chat.DTOs;

public record ArenaChatVoteRequest(Guid ChatSessionId, Guid BattleId, string Winner);
