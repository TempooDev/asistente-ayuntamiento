using System.Diagnostics;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.DTOs;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>Arena: both pipelines stream concurrently and the battle is stored (spec: question-arena).</summary>
public class ArenaStreamingServiceTests
{
    private readonly FakeRagPipeline _baseline = new(PipelineType.Baseline, "B1", "B2");
    private readonly FakeRagPipeline _hierarchical = new(PipelineType.Hierarchical, "H1", "H2", "H3");
    private readonly List<ArenaBattle> _saved = [];

    private ArenaStreamingService CreateSut()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRagPipeline>(_ => _baseline);
        services.AddScoped<IRagPipeline>(_ => _hierarchical);
        var provider = services.BuildServiceProvider();

        var battles = new Mock<DbSet<ArenaBattle>>();
        battles.Setup(b => b.Add(It.IsAny<ArenaBattle>())).Callback<ArenaBattle>(_saved.Add);
        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ArenaBattles).Returns(battles.Object);

        return new ArenaStreamingService(provider.GetRequiredService<IServiceScopeFactory>(), db.Object, NullLogger<ArenaStreamingService>.Instance);
    }

    private static ChatTurn Turn()
    {
        var history = new ChatHistory();
        history.AddUserMessage("¿Plazo de la ayuda?");
        return new ChatTurn(history, "org_A", "u1");
    }

    [Fact]
    public async Task Yields_the_battle_id_first_then_every_chunk_of_both_sides()
    {
        var chunks = await CreateSut().StreamAsync(Turn()).ToListAsync();

        chunks[0].Option.Should().Be(ArenaStreamingService.SessionIdOption);
        Guid.TryParse(chunks[0].Content, out _).Should().BeTrue();

        var bySide = chunks.Skip(1).GroupBy(c => c.Option).ToDictionary(g => g.Key, g => string.Concat(g.Select(c => c.Content)));
        bySide.Values.Order().Should().Equal("B1B2", "H1H2H3");
    }

    [Fact]
    public async Task Stores_the_battle_with_each_side_answer_and_its_pipeline()
    {
        var chunks = await CreateSut().StreamAsync(Turn()).ToListAsync();

        var battle = _saved.Should().ContainSingle().Subject;
        battle.SessionId.ToString().Should().Be(chunks[0].Content);
        battle.TenantId.Should().Be("org_A");
        battle.UserId.Should().Be("u1");
        battle.UserQuery.Should().Be("¿Plazo de la ayuda?");
        battle.Winner.Should().Be(BattleWinner.Pending);
        new[] { battle.LeftSystem, battle.RightSystem }.Order().Should().Equal(PipelineType.Baseline, PipelineType.Hierarchical);

        var alfa = string.Concat(chunks.Where(c => c.Option == ArenaStreamingService.AlfaOption).Select(c => c.Content));
        battle.LeftResponse.Should().Be(alfa);
        battle.LeftTokens.Should().Be(TokenUsageInfo.Estimate(alfa));
    }

    [Fact]
    public async Task A_slow_side_does_not_hold_back_the_other_one()
    {
        var release = new TaskCompletionSource();
        _hierarchical.Gate = release.Task;
        var sut = CreateSut();

        var received = new List<ArenaStreamChunk>();
        await foreach (var chunk in sut.StreamAsync(Turn()))
        {
            received.Add(chunk);
            // The baseline side finishes while the hierarchical side is still blocked.
            if (received.Count(c => c.Content.StartsWith('B')) == 2)
            {
                release.TrySetResult();
            }
        }

        received.Select(c => c.Content).Should().ContainInOrder("B1", "B2", "H1");
    }

    [Fact]
    public async Task Each_side_gets_its_own_copy_of_the_history()
    {
        var turn = Turn();

        await CreateSut().StreamAsync(turn).ToListAsync();

        _baseline.Turns.Single().History.Should().NotBeSameAs(turn.History);
        _hierarchical.Turns.Single().History.Should().NotBeSameAs(turn.History);
        _baseline.Turns.Single().History.Should().NotBeSameAs(_hierarchical.Turns.Single().History);
    }

    [Fact]
    public async Task Tags_each_side_with_its_arena_pipeline_for_the_metrics()
    {
        await CreateSut().StreamAsync(Turn()).ToListAsync();

        new[] { _baseline.Turns.Single().TelemetryPipeline, _hierarchical.Turns.Single().TelemetryPipeline }
            .Should().BeEquivalentTo([LlmPipelines.ArenaAlfa, LlmPipelines.ArenaBeta]);
    }

    [Fact]
    public async Task A_failing_side_is_reported_after_its_text_and_the_battle_is_not_stored()
    {
        _hierarchical.Failure = new InvalidOperationException("retrieval down");

        var chunks = await CreateSut().StreamAsync(Turn()).ToListAsync();

        var failed = chunks.Where(c => c.Content.StartsWith('H') || c.ErrorCode is not null).ToList();
        failed.Select(c => c.Option).Distinct().Should().ContainSingle();
        failed.Select(c => c.Content).Should().Equal("H1", "H2", "H3", string.Empty);
        failed[^1].ErrorCode.Should().Be(ArenaStreamingService.SideFailedCode);
        chunks.Where(c => c.Content.StartsWith('B')).Should().OnlyContain(c => c.ErrorCode == null).And.HaveCount(2);
        _saved.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_interruption_reports_its_typed_code()
    {
        _baseline.Failure = new LlmStreamInterruptedException(LlmErrorCodes.QuotaExceeded, new IOException("429"));

        var chunks = await CreateSut().StreamAsync(Turn()).ToListAsync();

        chunks.Where(c => c.ErrorCode is not null).Should().ContainSingle().Which.ErrorCode.Should().Be(LlmErrorCodes.QuotaExceeded);
        _saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Stores_the_trace_id_so_a_vote_can_be_linked_to_its_traces()
    {
        using var activity = new Activity("chat").Start();

        await CreateSut().StreamAsync(Turn()).ToListAsync();

        _saved.Single().TraceId.Should().Be(activity.TraceId.ToHexString());
    }
}
