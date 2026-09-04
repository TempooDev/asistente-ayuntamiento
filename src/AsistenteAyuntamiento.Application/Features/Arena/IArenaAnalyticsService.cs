using AsistenteAyuntamiento.Application.Features.Arena.Models;

namespace AsistenteAyuntamiento.Application.Features.Arena;

public interface IArenaAnalyticsService
{
    Task<ArenaAnalyticsResponse> GetAnalyticsAsync(CancellationToken cancellationToken = default);
}
