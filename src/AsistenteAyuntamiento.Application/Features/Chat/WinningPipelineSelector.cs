using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// The pipeline that answers the chat: the one set in <c>Rag:ProductionPipeline</c> (any registered variant, for
/// example the winner of the chunking study), otherwise the one with more Arena wins (ties and no votes:
/// <see cref="PipelineType.Baseline"/>). Wins are counted in the database and cached briefly, since they are read on
/// every streamed answer.
/// </summary>
public sealed class WinningPipelineSelector(IAppDbContext dbContext, IMemoryCache cache, IConfiguration? configuration = null) : IWinningPipelineSelector
{
    internal const string CacheKey = "arena:winning-pipeline";
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public const string ProductionPipelineKey = "Rag:ProductionPipeline";

    public async Task<PipelineType> GetWinnerAsync(CancellationToken cancellationToken = default)
    {
        if (Enum.TryParse<PipelineType>(configuration?[ProductionPipelineKey], ignoreCase: true, out var configured)
            && Enum.IsDefined(configured))
        {
            return configured;
        }

        if (cache.TryGetValue(CacheKey, out PipelineType cached))
        {
            return cached;
        }

        // Cross-tenant on purpose: only aggregate win counts are read (no battle content),
        // and the winning pipeline is a platform-wide decision.
        var wins = await dbContext.ArenaBattles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => b.Winner == BattleWinner.Alfa || b.Winner == BattleWinner.Beta)
            .Select(b => b.Winner == BattleWinner.Alfa ? b.LeftSystem : b.RightSystem)
            .GroupBy(system => system)
            .Select(g => new { System = g.Key, Wins = g.Count() })
            .ToListAsync(cancellationToken);

        int WinsOf(PipelineType type) => wins.FirstOrDefault(w => w.System == type)?.Wins ?? 0;
        var winner = WinsOf(PipelineType.Hierarchical) > WinsOf(PipelineType.Baseline) ? PipelineType.Hierarchical : PipelineType.Baseline;

        cache.Set(CacheKey, winner, CacheDuration);
        return winner;
    }
}
