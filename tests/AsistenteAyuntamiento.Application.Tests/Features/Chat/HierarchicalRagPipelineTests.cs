using System.Runtime.CompilerServices;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Domain.Common.Enums;
using FluentAssertions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>The hierarchical pipeline's evaluation run.</summary>
public class HierarchicalRagPipelineTests
{
    private readonly Mock<IQueryExpansionService> _expansion = new();
    private readonly Mock<IHybridRetrievalService> _retrieval = new();
    private readonly Mock<IClearLanguageGenerationService> _generation = new();
    private LlmCallScope? _answerScope;

    private static readonly List<RetrievalResult> Retrieved =
    [
        new(10, "Fragmento A", 1, "Texto completo A", 0.03, "BOE-A-2026-1", "Orden A", BulletinType.BOE),
        new(11, "Fragmento B", 2, "Texto completo B", 0.02, "BOJA-2026-1-1", "Orden B", BulletinType.BOJA),
    ];

    private HierarchicalRagPipeline CreateSut(Exception? generationFailure = null)
    {
        _expansion.Setup(e => e.ExpandQueryAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatMessageContent>>(), It.IsAny<LlmCallScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpandedQueryInfo { SemanticQuery = "ayudas alquiler", LexicalQuery = "ayudas alquiler" });
        _retrieval.Setup(r => r.RetrieveAsync(It.IsAny<ExpandedQueryInfo>(), 5, It.IsAny<CancellationToken>())).ReturnsAsync(Retrieved);
        _generation.Setup(g => g.BuildPromptContext(Retrieved)).Returns("CONTEXTO");
        _generation.Setup(g => g.GenerateStreamingResponseAsync(It.IsAny<string>(), Retrieved, It.IsAny<LlmCallScope>(),
                It.IsAny<IReadOnlyList<ChatMessageContent>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<RetrievalResult>, LlmCallScope, IReadOnlyList<ChatMessageContent>?, CancellationToken>((_, _, scope, _, _) => _answerScope = scope)
            .Returns(Stream(generationFailure, "Hola", " mundo", "\n\n### Fuentes consultadas\n- [Orden A](https://www.boe.es)"));

        return new HierarchicalRagPipeline(_expansion.Object, _retrieval.Object, _generation.Object);
    }

    private static async IAsyncEnumerable<StreamingChatMessageContent> Stream(Exception? failure, params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            if (failure is not null)
            {
                throw failure;
            }
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk);
        }
    }

    [Fact]
    public async Task Evaluation_returns_fragments_as_contexts_and_the_parents_as_generation_context()
    {
        var result = await CreateSut().RunForEvaluationAsync(new EvaluationTurn("¿Ayudas al alquiler?"));

        result.Pipeline.Should().Be(PipelineType.Hierarchical);
        result.ErrorCode.Should().BeNull();
        result.Answer.Should().Be("Hola mundo");
        result.Contexts.Should().Equal(new RetrievedContext("BOE-A-2026-1", 0.03, "Fragmento A"), new RetrievedContext("BOJA-2026-1-1", 0.02, "Fragmento B"));
        result.GenerationContext.Should().Be("CONTEXTO");
        result.Timings.TotalMs.Should().BeGreaterThanOrEqualTo(result.Timings.ExpansionMs + result.Timings.RetrievalMs + result.Timings.GenerationMs);
        _answerScope!.Pipeline.Should().Be(LlmPipelines.Evaluation);
        _answerScope.UserId.Should().BeNull("evaluation answers are not stored as a user's AI calls");
    }

    [Fact]
    public async Task Evaluation_returns_model_failures_as_an_error_code()
    {
        var result = await CreateSut(new LlmStreamInterruptedException(LlmErrorCodes.QuotaExceeded, new HttpRequestException("429")))
            .RunForEvaluationAsync(new EvaluationTurn("¿Ayudas?"));

        result.ErrorCode.Should().Be(LlmErrorCodes.QuotaExceeded);
        result.Answer.Should().BeEmpty();
        result.Contexts.Should().HaveCount(2);
    }
}
