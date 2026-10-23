using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Application.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>How the baseline pipeline injects retrieved context and user preferences into the prompt.</summary>
public class BaselineRagPipelineTests
{
    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<IChatStreamingService> _streaming = new();
    private readonly Mock<IUserPreferenceService> _userPreferences = new();
    private ChatHistory? _sentHistory;
    private string? _sentPipeline;
    private string? _embeddedText;

    private BaselineRagPipeline CreateSut(IReadOnlyList<VectorSearchHit> hits, UserPreferenceDto? preferences = null, string? queryInstruction = null)
    {
        var embeddings = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        embeddings.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>((texts, _, _) => _embeddedText = texts.Single())
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[] { 1f, 0f })]));
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(embeddings.Object);
        if (queryInstruction is not null)
        {
            kernelBuilder.Services.AddSingleton(new QueryEmbeddingOptions(queryInstruction));
        }

        _vectorStore.Setup(v => v.SearchAsync(VectorCollection.DocumentChunks, It.IsAny<VectorSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hits);

        _streaming.Setup(s => s.StreamAsync(It.IsAny<ChatHistory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<ChatHistory, string, string, string, CancellationToken>((history, _, _, pipeline, _) =>
            {
                _sentHistory = history;
                _sentPipeline = pipeline;
            })
            .Returns(new[] { "ok" }.ToAsyncEnumerable());

        _userPreferences.Setup(p => p.GetPreferencesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(preferences ?? new UserPreferenceDto());

        return new BaselineRagPipeline(kernelBuilder.Build(), _vectorStore.Object, _userPreferences.Object, _streaming.Object, NullLogger<BaselineRagPipeline>.Instance);
    }

    private static VectorSearchHit Hit(string title) => new(1, 0.9f, new Dictionary<string, object?>
    {
        [VectorPayloadKeys.Title] = title,
        [VectorPayloadKeys.Source] = "BOE",
        [VectorPayloadKeys.DocumentId] = "BOE-A-2026-1",
        [VectorPayloadKeys.Department] = "Ministerio",
        [VectorPayloadKeys.Content] = "Texto del boletín",
        [VectorPayloadKeys.PublicationDate] = "2026-10-01T00:00:00.0000000Z",
    });

    private static ChatTurn Turn(string question = "¿Ayudas al alquiler?")
    {
        var history = new ChatHistory();
        history.AddUserMessage(question);
        return new ChatTurn(history, "org_A", "u1");
    }

    [Fact]
    public async Task Streaming_with_matches_injects_the_context_and_a_system_prompt()
    {
        await CreateSut([Hit("Bono alquiler")]).StreamAsync(Turn()).ToListAsync();

        _sentHistory.Should().NotBeNull();
        _sentHistory![0].Role.Should().Be(AuthorRole.System);
        _sentHistory[0].Content.Should().StartWith(PromptCatalog.StreamingSystem);
        var question = _sentHistory[^1].Content;
        question.Should().Contain("Bono alquiler").And.Contain("https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1")
            .And.Contain("Fecha: 2026-10-01").And.EndWith("Pregunta: ¿Ayudas al alquiler?");
        _vectorStore.Verify(v => v.SearchAsync(VectorCollection.DocumentChunks, It.Is<VectorSearchRequest>(r => r.Limit == 3 && r.ScoreThreshold == 0.65f), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task The_answer_ends_with_the_sources_of_the_documents_found()
    {
        var chunks = await CreateSut([Hit("Bono alquiler"), Hit("Bono alquiler")]).StreamAsync(Turn()).ToListAsync();

        string.Concat(chunks).Should().Be("ok\n\n### Fuentes consultadas\n" +
            "- [Bono alquiler](https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1) (BOE-A-2026-1)");
    }

    [Fact]
    public async Task Without_documents_the_answer_has_no_sources()
    {
        var chunks = await CreateSut([]).StreamAsync(Turn()).ToListAsync();

        chunks.Should().Equal("ok");
    }

    [Fact]
    public async Task Labels_the_call_as_baseline_unless_the_caller_set_a_pipeline()
    {
        await CreateSut([]).StreamAsync(Turn()).ToListAsync();
        _sentPipeline.Should().Be(LlmPipelines.Baseline);

        await CreateSut([]).StreamAsync(Turn() with { TelemetryPipeline = LlmPipelines.Shadow }).ToListAsync();
        _sentPipeline.Should().Be(LlmPipelines.Shadow);
    }

    [Fact]
    public async Task Streaming_without_matches_keeps_the_question_and_adds_the_conversational_prompt()
    {
        await CreateSut([]).StreamAsync(Turn()).ToListAsync();

        _sentHistory!.Select(m => m.Role).Should().Equal(AuthorRole.System, AuthorRole.User);
        _sentHistory![0].Content.Should().Be(PromptCatalog.ConversationalSystem);
        _sentHistory![1].Content.Should().Be("¿Ayudas al alquiler?");
    }

    [Fact]
    public async Task User_preferences_are_added_to_the_system_prompt()
    {
        var preferences = new UserPreferenceDto { Topics = ["Vivienda"], Locations = ["Málaga"] };

        await CreateSut([Hit("Bono")], preferences).StreamAsync(Turn()).ToListAsync();

        _sentHistory![0].Content.Should().Contain("Temas de interés principales: Vivienda").And.Contain("Zonas geográficas de interés: Málaga");
    }

    [Fact]
    public async Task A_failed_search_answers_without_context()
    {
        var sut = CreateSut([]);
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<VectorCollection>(), It.IsAny<VectorSearchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("qdrant down"));

        var chunks = await sut.StreamAsync(Turn()).ToListAsync();

        chunks.Should().Equal("ok");
        _sentHistory!.Single(m => m.Role == AuthorRole.User).Content.Should().Be("¿Ayudas al alquiler?", "no context was injected");
    }

    [Fact]
    public async Task Queries_carry_the_configured_instruction()
    {
        await CreateSut([], queryInstruction: "Retrieve bulletin passages").StreamAsync(Turn()).ToListAsync();

        _embeddedText.Should().Be("Instruct: Retrieve bulletin passages\nQuery: ¿Ayudas al alquiler?");
    }

    [Fact]
    public async Task Without_an_instruction_the_query_is_embedded_as_written()
    {
        await CreateSut([]).StreamAsync(Turn()).ToListAsync();

        _embeddedText.Should().Be("¿Ayudas al alquiler?");
    }

    [Fact]
    public async Task Evaluation_returns_the_answer_contexts_and_timings_without_sources()
    {
        var result = await CreateSut([Hit("Bono alquiler")]).RunForEvaluationAsync(new EvaluationTurn("¿Ayudas al alquiler?"));

        result.Pipeline.Should().Be(Domain.Common.Enums.PipelineType.Baseline);
        result.ErrorCode.Should().BeNull();
        result.Answer.Should().Be("ok", "the sources block is built from the retrieval, not generated");
        result.Contexts.Should().ContainSingle().Which.Should().Be(new RetrievedContext("BOE-A-2026-1", 0.9f, "Texto del boletín"));
        result.GenerationContext.Should().Contain("Bono alquiler").And.Contain("Texto del boletín");
        result.Timings.ExpansionMs.Should().Be(0);
        result.Timings.TimeToFirstTokenMs.Should().NotBeNull();
        result.Timings.TotalMs.Should().BeGreaterThanOrEqualTo(result.Timings.RetrievalMs + result.Timings.GenerationMs);
        _sentPipeline.Should().Be(LlmPipelines.Evaluation);
        _sentHistory![0].Content.Should().StartWith(PromptCatalog.StreamingSystem);
        _vectorStore.Verify(v => v.SearchAsync(VectorCollection.DocumentChunks, It.Is<VectorSearchRequest>(r => r.Limit == 5 && r.ScoreThreshold == null), It.IsAny<CancellationToken>()));
        _userPreferences.Verify(p => p.GetPreferencesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Evaluation_returns_model_failures_as_an_error_code()
    {
        var sut = CreateSut([Hit("Bono")]);
        _streaming.Setup(s => s.StreamAsync(It.IsAny<ChatHistory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Failing(new LlmStreamInterruptedException(LlmErrorCodes.Unavailable, new HttpRequestException("down"))));

        var result = await sut.RunForEvaluationAsync(new EvaluationTurn("¿Ayudas?"));

        result.ErrorCode.Should().Be(LlmErrorCodes.Unavailable);
        result.Answer.Should().BeEmpty();
        result.Contexts.Should().ContainSingle();
    }

    private static async IAsyncEnumerable<string> Failing(Exception exception)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162 // An iterator needs a yield.
        yield break;
#pragma warning restore CS0162
    }
}
