using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Domain.Common.Enums;
using FluentAssertions;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>The streaming facade answers with the winning pipeline and hands shadow runs to the scheduler.</summary>
public class AiChatServiceStreamingTests
{
    private readonly FakeRagPipeline _baseline = new(PipelineType.Baseline, "base");
    private readonly FakeRagPipeline _hierarchical = new(PipelineType.Hierarchical, "jer", "árquica");
    private readonly Mock<IWinningPipelineSelector> _selector = new();
    private readonly Mock<IShadowTestScheduler> _shadow = new();

    private AiChatService CreateSut(PipelineType winner, bool shadow)
    {
        _selector.Setup(s => s.GetWinnerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(winner);
        _shadow.Setup(s => s.ShouldRun()).Returns(shadow);
        return new AiChatService(null!, [_baseline, _hierarchical], _selector.Object, _shadow.Object, Mock.Of<IArenaStreamingService>());
    }

    private static ChatHistory History()
    {
        var history = new ChatHistory();
        history.AddUserMessage("¿Horario?");
        return history;
    }

    [Fact]
    public async Task Streams_the_winning_pipeline_only()
    {
        var chunks = await CreateSut(PipelineType.Hierarchical, shadow: false).GetStreamingCompletionAsync(History(), "org_A", "u1").ToListAsync();

        chunks.Should().Equal("jer", "árquica");
        _baseline.Turns.Should().BeEmpty();
        _shadow.Verify(s => s.Schedule(It.IsAny<ChatTurn>(), It.IsAny<PipelineType>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task A_sampled_answer_schedules_a_shadow_run_with_an_untouched_copy_of_the_turn()
    {
        var history = History();

        await CreateSut(PipelineType.Baseline, shadow: true).GetStreamingCompletionAsync(history, "org_A", "u1").ToListAsync();

        _shadow.Verify(s => s.Schedule(
            It.Is<ChatTurn>(t => t.History != history && t.TenantId == "org_A" && t.UserQuery == "¿Horario?"),
            PipelineType.Baseline,
            "base",
            It.IsAny<long>()), Times.Once);
    }
}
