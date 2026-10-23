using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MockQueryable.Moq;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

public class WinningPipelineSelectorTests
{
    private static ArenaBattle Battle(PipelineType left, PipelineType right, BattleWinner winner) =>
        new() { LeftSystem = left, RightSystem = right, Winner = winner };

    private static (WinningPipelineSelector Sut, Mock<IAppDbContext> Db) Create(params ArenaBattle[] battles)
    {
        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ArenaBattles).Returns(battles.ToList().BuildMockDbSet().Object);
        return (new WinningPipelineSelector(db.Object, new MemoryCache(new MemoryCacheOptions())), db);
    }

    [Fact]
    public async Task Without_votes_the_baseline_answers()
    {
        var (sut, _) = Create(Battle(PipelineType.Hierarchical, PipelineType.Baseline, BattleWinner.Pending));

        (await sut.GetWinnerAsync()).Should().Be(PipelineType.Baseline);
    }

    [Fact]
    public async Task The_pipeline_with_more_wins_answers_regardless_of_the_side_it_was_on()
    {
        var (sut, _) = Create(
            Battle(PipelineType.Hierarchical, PipelineType.Baseline, BattleWinner.Alfa),
            Battle(PipelineType.Baseline, PipelineType.Hierarchical, BattleWinner.Beta),
            Battle(PipelineType.Baseline, PipelineType.Hierarchical, BattleWinner.Alfa),
            Battle(PipelineType.Baseline, PipelineType.Hierarchical, BattleWinner.Tie));

        (await sut.GetWinnerAsync()).Should().Be(PipelineType.Hierarchical);
    }

    [Fact]
    public async Task A_tie_keeps_the_baseline()
    {
        var (sut, _) = Create(
            Battle(PipelineType.Hierarchical, PipelineType.Baseline, BattleWinner.Alfa),
            Battle(PipelineType.Hierarchical, PipelineType.Baseline, BattleWinner.Beta));

        (await sut.GetWinnerAsync()).Should().Be(PipelineType.Baseline);
    }

    [Fact]
    public async Task The_result_is_cached_between_calls()
    {
        var (sut, db) = Create(Battle(PipelineType.Hierarchical, PipelineType.Baseline, BattleWinner.Alfa));

        await sut.GetWinnerAsync();
        await sut.GetWinnerAsync();

        db.Verify(d => d.ArenaBattles, Times.Once);
    }

    [Theory]
    [InlineData("Semantic", PipelineType.Semantic)]
    [InlineData("recursive", PipelineType.Recursive)]
    public async Task A_configured_production_pipeline_answers_whatever_the_votes(string setting, PipelineType expected)
    {
        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ArenaBattles).Returns(new List<ArenaBattle> { Battle(PipelineType.Hierarchical, PipelineType.Baseline, BattleWinner.Alfa) }.BuildMockDbSet().Object);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [WinningPipelineSelector.ProductionPipelineKey] = setting }).Build();

        (await new WinningPipelineSelector(db.Object, new MemoryCache(new MemoryCacheOptions()), config).GetWinnerAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task An_unknown_production_pipeline_falls_back_to_the_votes()
    {
        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ArenaBattles).Returns(new List<ArenaBattle> { Battle(PipelineType.Hierarchical, PipelineType.Baseline, BattleWinner.Alfa) }.BuildMockDbSet().Object);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [WinningPipelineSelector.ProductionPipelineKey] = "Nope" }).Build();

        (await new WinningPipelineSelector(db.Object, new MemoryCache(new MemoryCacheOptions()), config).GetWinnerAsync()).Should().Be(PipelineType.Hierarchical);
    }
}
