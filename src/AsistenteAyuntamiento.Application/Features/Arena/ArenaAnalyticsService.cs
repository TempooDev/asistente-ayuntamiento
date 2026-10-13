using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Arena.Models;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Arena;

public class ArenaAnalyticsService(IAppDbContext dbContext) : IArenaAnalyticsService
{
    public async Task<ArenaAnalyticsResponse> GetAnalyticsAsync(ArenaAnalyticsRequest? request = null, CancellationToken cancellationToken = default)
    {
        // Cross-tenant on purpose: the endpoint is admin-only and returns aggregates only
        // (win rates, latency, tokens), never battle content.
        var query = dbContext.ArenaBattles.IgnoreQueryFilters().AsNoTracking();

        if (request != null)
        {
            if (request.StartDate.HasValue)
            {
                query = query.Where(b => b.CreatedAt >= request.StartDate.Value.ToUniversalTime());
            }

            if (request.EndDate.HasValue)
            {
                query = query.Where(b => b.CreatedAt <= request.EndDate.Value.ToUniversalTime());
            }
        }

        var battles = await query.ToListAsync(cancellationToken);

        var total = battles.Count;
        var pending = battles.Count(b => b.Winner == BattleWinner.Pending);
        var completed = total - pending;

        var pipelines = Enum.GetValues<PipelineType>();
        var metricsList = new List<PipelineMetrics>();

        foreach (var p in pipelines)
        {
            var asLeft = battles.Where(b => b.LeftSystem == p).ToList();
            var asRight = battles.Where(b => b.RightSystem == p).ToList();

            var winCount = asLeft.Count(b => b.Winner == BattleWinner.Alfa) + asRight.Count(b => b.Winner == BattleWinner.Beta);
            var lossCount = asLeft.Count(b => b.Winner == BattleWinner.Beta) + asRight.Count(b => b.Winner == BattleWinner.Alfa);
            var tieCount = asLeft.Count(b => b.Winner == BattleWinner.Tie) + asRight.Count(b => b.Winner == BattleWinner.Tie);
            var bothBadCount = asLeft.Count(b => b.Winner == BattleWinner.BothBad) + asRight.Count(b => b.Winner == BattleWinner.BothBad);

            var totalAsSystem = asLeft.Count + asRight.Count;
            double winRate = 0;
            double avgLatency = 0;
            double avgTokens = 0;

            if (totalAsSystem > 0)
            {
                // Win rate over decisive votes only (a winner was picked); ties and "both bad" are reported apart.
                var decisiveVotes = winCount + lossCount;
                if (decisiveVotes > 0)
                {
                    winRate = (double)winCount / decisiveVotes;
                }

                var totalLatency = asLeft.Sum(b => b.LeftLatencyMs) + asRight.Sum(b => b.RightLatencyMs);
                var totalTokens = asLeft.Sum(b => b.LeftTokens) + asRight.Sum(b => b.RightTokens);

                avgLatency = (double)totalLatency / totalAsSystem;
                avgTokens = (double)totalTokens / totalAsSystem;
            }

            metricsList.Add(new PipelineMetrics
            {
                Pipeline = p.ToString(),
                WinCount = winCount,
                LossCount = lossCount,
                TieCount = tieCount,
                BothBadCount = bothBadCount,
                WinRate = winRate,
                AverageLatencyMs = avgLatency,
                AverageTokens = avgTokens
            });
        }

        return new ArenaAnalyticsResponse
        {
            TotalBattles = total,
            PendingBattles = pending,
            CompletedBattles = completed,
            Metrics = metricsList
        };
    }
}

