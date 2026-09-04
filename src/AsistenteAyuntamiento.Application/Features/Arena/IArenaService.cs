using AsistenteAyuntamiento.Application.Features.Arena.Models;

namespace AsistenteAyuntamiento.Application.Features.Arena;

public interface IArenaService
{
    Task<ArenaCompareResponse> CompareAsync(ArenaCompareRequest request, CancellationToken cancellationToken = default);
    Task VoteAsync(ArenaVoteRequest request, CancellationToken cancellationToken = default);
}
