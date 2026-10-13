using AsistenteAyuntamiento.Application.Common.Background;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>Shadow tests run on the tracked background queue, never as fire-and-forget tasks.</summary>
public class ShadowTestSchedulerTests
{
    private readonly List<BackgroundWorkItem> _queued = [];
    private readonly List<ArenaBattle> _saved = [];

    private ShadowTestScheduler CreateSut(bool queueAccepts = true)
    {
        var queue = new Mock<IBackgroundWorkQueue>();
        queue.Setup(q => q.TryEnqueue(It.IsAny<BackgroundWorkItem>()))
            .Callback<BackgroundWorkItem>(_queued.Add)
            .Returns(queueAccepts);
        return new ShadowTestScheduler(queue.Object, NullLogger<ShadowTestScheduler>.Instance);
    }

    private IServiceProvider Services(params IRagPipeline[] pipelines)
    {
        var battles = new Mock<DbSet<ArenaBattle>>();
        battles.Setup(b => b.Add(It.IsAny<ArenaBattle>())).Callback<ArenaBattle>(_saved.Add);
        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ArenaBattles).Returns(battles.Object);

        var services = new ServiceCollection();
        foreach (var pipeline in pipelines)
        {
            services.AddSingleton(pipeline);
        }
        services.AddSingleton(db.Object);
        return services.BuildServiceProvider();
    }

    private static ChatTurn Turn()
    {
        var history = new ChatHistory();
        history.AddUserMessage("¿Requisitos?");
        return new ChatTurn(history, "org_A", "u1");
    }

    [Fact]
    public void Schedule_queues_tenant_scoped_work_instead_of_running_it()
    {
        CreateSut().Schedule(Turn(), PipelineType.Baseline, "respuesta", 120);

        _queued.Should().ContainSingle().Which.TenantId.Should().Be("org_A");
        _saved.Should().BeEmpty();
    }

    [Fact]
    public async Task The_queued_work_runs_the_other_pipeline_and_stores_both_answers()
    {
        var hierarchical = new FakeRagPipeline(PipelineType.Hierarchical, "sombra");
        CreateSut().Schedule(Turn(), PipelineType.Baseline, "respuesta", 120);

        await _queued.Single().Work(Services(hierarchical), CancellationToken.None);

        hierarchical.Turns.Should().ContainSingle();
        var battle = _saved.Should().ContainSingle().Subject;
        var answers = new Dictionary<PipelineType, string>
        {
            [battle.LeftSystem] = battle.LeftResponse,
            [battle.RightSystem] = battle.RightResponse,
        };
        answers.Should().BeEquivalentTo(new Dictionary<PipelineType, string>
        {
            [PipelineType.Baseline] = "respuesta",
            [PipelineType.Hierarchical] = "sombra",
        });
        battle.Winner.Should().Be(BattleWinner.Pending);
    }

    [Fact]
    public void A_full_queue_skips_the_shadow_test_without_throwing()
    {
        var act = () => CreateSut(queueAccepts: false).Schedule(Turn(), PipelineType.Hierarchical, "respuesta", 10);

        act.Should().NotThrow();
    }
}
