using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Arena.Models;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Arena;

public class ArenaAnalyticsServiceTests
{
    private static ArenaBattle Battle(PipelineType left, PipelineType right, BattleWinner winner, int leftMs = 0, int rightMs = 0, DateTime? at = null) => new()
    {
        LeftSystem = left,
        RightSystem = right,
        Winner = winner,
        LeftLatencyMs = leftMs,
        RightLatencyMs = rightMs,
        LeftTokens = 10,
        RightTokens = 30,
        CreatedAt = at ?? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static ArenaAnalyticsService Sut(params ArenaBattle[] battles)
    {
        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ArenaBattles).Returns(battles.ToList().BuildMockDbSet().Object);
        return new ArenaAnalyticsService(db.Object);
    }

    [Fact]
    public async Task Wins_losses_and_ties_are_attributed_to_the_pipeline_whatever_its_side()
    {
        var analytics = await Sut(
            Battle(PipelineType.Hierarchical, PipelineType.Baseline, BattleWinner.Alfa, leftMs: 100, rightMs: 300),
            Battle(PipelineType.Baseline, PipelineType.Hierarchical, BattleWinner.Beta, leftMs: 200, rightMs: 300),
            Battle(PipelineType.Baseline, PipelineType.Hierarchical, BattleWinner.Tie),
            Battle(PipelineType.Baseline, PipelineType.Hierarchical, BattleWinner.Pending)).GetAnalyticsAsync();

        analytics.TotalBattles.Should().Be(4);
        analytics.PendingBattles.Should().Be(1);
        analytics.CompletedBattles.Should().Be(3);

        var hierarchical = analytics.Metrics.Single(m => m.Pipeline == nameof(PipelineType.Hierarchical));
        hierarchical.WinCount.Should().Be(2);
        hierarchical.LossCount.Should().Be(0);
        hierarchical.TieCount.Should().Be(1);
        hierarchical.WinRate.Should().BeApproximately(2.0 / 3, 1e-9);
        hierarchical.AverageLatencyMs.Should().Be((100 + 300 + 0 + 0) / 4.0);

        var baseline = analytics.Metrics.Single(m => m.Pipeline == nameof(PipelineType.Baseline));
        baseline.LossCount.Should().Be(2);
        baseline.WinRate.Should().Be(0);
    }

    [Fact]
    public async Task The_date_range_limits_the_battles_counted()
    {
        var analytics = await Sut(
            Battle(PipelineType.Baseline, PipelineType.Hierarchical, BattleWinner.Alfa, at: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            Battle(PipelineType.Baseline, PipelineType.Hierarchical, BattleWinner.Alfa, at: new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc)))
            .GetAnalyticsAsync(new ArenaAnalyticsRequest { StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) });

        analytics.TotalBattles.Should().Be(1);
    }
}
