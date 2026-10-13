using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Generation;

/// <summary>The hierarchical answer of a user is recorded in AiCallLogs like the baseline's.</summary>
public class ClearLanguageGenerationServiceTests
{
    private static readonly List<RetrievalResult> Context = [new(1, "fragmento", 10, "Texto del documento", 1.0)];

    private readonly Mock<IChatCompletionService> _chat = new();
    private readonly Mock<ILlmUsageMetrics> _metrics = new();
    private readonly List<AiCallOutcome> _recorded = [];
    private readonly ClearLanguageGenerationService _sut;

    public ClearLanguageGenerationServiceTests()
    {
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(_chat.Object);

        var recorder = new Mock<IAiCallRecorder>();
        recorder.Setup(r => r.RecordAsync(It.IsAny<AiCallOutcome>(), It.IsAny<CancellationToken>()))
            .Callback<AiCallOutcome, CancellationToken>((outcome, _) => _recorded.Add(outcome))
            .Returns(Task.CompletedTask);

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Chat:Provider"] = "openai",
            ["Ai:Chat:Model"] = "gpt-4o-mini",
        }).Build();

        _sut = new ClearLanguageGenerationService(builder.Build(), configuration, _metrics.Object, recorder.Object,
            NullLogger<ClearLanguageGenerationService>.Instance);
    }

    private void Streams(params string[] chunks) =>
        _chat.Setup(s => s.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .Returns(chunks.Select(c => new StreamingChatMessageContent(AuthorRole.Assistant, c)).ToAsyncEnumerable());

    private async Task<string> StreamAsync(LlmCallScope scope)
    {
        var answer = new System.Text.StringBuilder();
        await foreach (var chunk in _sut.GenerateStreamingResponseAsync("¿Qué plazo hay?", Context, scope))
        {
            answer.Append(chunk.Content);
        }
        return answer.ToString();
    }

    [Fact]
    public async Task A_users_answer_is_recorded_with_the_platform_model_tenant_user_and_pipeline()
    {
        Streams("Tienes ", "diez días.");

        var answer = await StreamAsync(new LlmCallScope("org_A", LlmPipelines.Hierarchical) { UserId = "auth0|ana" });

        answer.Should().Be("Tienes diez días.");
        _recorded.Should().ContainSingle().Which.Should().Match<AiCallOutcome>(o =>
            o.ModelId == "gpt-4o-mini" && o.Provider == "openai" && o.TenantId == "org_A" && o.UserId == "auth0|ana"
            && o.Pipeline == LlmPipelines.Hierarchical && o.Success && o.ResponseLength == answer.Length);
        _metrics.Verify(m => m.Record(It.IsAny<LlmCallMetrics>()), Times.Never, "the recorder emits the metrics");
    }

    [Fact]
    public async Task A_failed_stream_is_recorded_as_a_failure()
    {
        _chat.Setup(s => s.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("provider down"));

        await StreamAsync(new LlmCallScope("org_A", LlmPipelines.Hierarchical) { UserId = "auth0|ana" });

        _recorded.Should().ContainSingle().Which.Should().Match<AiCallOutcome>(o => !o.Success && o.ErrorDetail == "provider down");
    }

    [Fact]
    public async Task Without_a_user_only_the_metrics_are_recorded()
    {
        Streams("Respuesta");

        await StreamAsync(new LlmCallScope("org_A", LlmPipelines.Hierarchical));

        _recorded.Should().BeEmpty();
        _metrics.Verify(m => m.Record(It.Is<LlmCallMetrics>(c => c.TenantId == "org_A" && c.ErrorType == null)), Times.Once);
    }
}
