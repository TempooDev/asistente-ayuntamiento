namespace AsistenteAyuntamiento.Application.Features.Arena;

/// <summary>The battle already has a vote. Votes are final: the battle is left unchanged.</summary>
public sealed class ArenaVoteConflictException(Guid sessionId)
    : InvalidOperationException($"Arena battle {sessionId} has already been voted.");
