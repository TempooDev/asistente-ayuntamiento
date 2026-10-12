using AsistenteAyuntamiento.Application.Features.Arena.Models;

namespace AsistenteAyuntamiento.Application.Features.Arena;

public interface IArenaService
{
    Task<ArenaCompareResponse> CompareAsync(ArenaCompareRequest request, string tenantId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a vote on a battle owned by <paramref name="userId"/> in the current tenant.
    /// Returns <c>null</c> when the battle does not exist or belongs to someone else.
    /// </summary>
    /// <exception cref="ArgumentException">The winner value is not valid.</exception>
    Task<ArenaVoteResponse?> VoteAsync(ArenaVoteRequest request, string userId, CancellationToken cancellationToken = default);
}
