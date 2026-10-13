using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using OpenAIChatModelFactory = OpenAI.Chat.OpenAIChatModelFactory;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat.Telemetry;

/// <summary>Platform model calls (query expansion, enrichment, history analysis, Arena) are measured like the chat.</summary>
public class MeteredChatCompletionTests
{
    private static readonly (string Provider, string Model) Model = ("openai", "gpt-4o-mini");
    private static readonly LlmCallScope Scope = new(LlmCallScope.PlatformTenant, LlmPipelines.Ingestion, LlmOperations.Enrichment);

    private readonly Mock<IChatCompletionService> _service = new();
    private readonly List<LlmCallMetrics> _recorded = [];
    private readonly Mock<ILlmUsageMetrics> _metrics = new();

    public MeteredChatCompletionTests() =>
        _metrics.Setup(m => m.Record(It.IsAny<LlmCallMetrics>())).Callback<LlmCallMetrics>(_recorded.Add);

    private void Answers(ChatMessageContent answer) =>
        _service.Setup(s => s.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([answer]);

    [Fact]
    public async Task Records_tokens_tenant_pipeline_and_operation_of_a_successful_call()
    {
        var usage = OpenAIChatModelFactory.ChatTokenUsage(outputTokenCount: 40, inputTokenCount: 300, totalTokenCount: 340);
        Answers(new ChatMessageContent(AuthorRole.Assistant, "¿Qué plazo hay?", metadata: new Dictionary<string, object?> { ["Usage"] = usage }));

        var result = await _service.Object.CompleteMeteredAsync("Genera preguntas", Model, Scope, _metrics.Object);

        result.Content.Should().Be("¿Qué plazo hay?");
        var call = _recorded.Should().ContainSingle().Subject;
        call.Should().Match<LlmCallMetrics>(c =>
            c.Provider == "openai" && c.Model == "gpt-4o-mini" && c.TenantId == LlmCallScope.PlatformTenant
            && c.Pipeline == LlmPipelines.Ingestion && c.Operation == LlmOperations.Enrichment
            && c.InputTokens == 300 && c.OutputTokens == 40 && c.ErrorType == null);
    }

    [Fact]
    public async Task Records_a_failed_call_with_its_error_type_and_rethrows()
    {
        _service.Setup(s => s.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));

        var act = () => _service.Object.CompleteMeteredAsync(new ChatHistory(), Model, Scope, _metrics.Object);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _recorded.Should().ContainSingle().Which.ErrorType.Should().Be(LlmErrorCodes.Unavailable);
    }

    [Fact]
    public async Task A_call_made_for_a_user_is_stored_through_the_recorder_with_its_step()
    {
        var outcomes = new List<AiCallOutcome>();
        var recorder = new Mock<IAiCallRecorder>();
        recorder.Setup(r => r.RecordAsync(It.IsAny<AiCallOutcome>(), It.IsAny<CancellationToken>()))
            .Callback<AiCallOutcome, CancellationToken>((o, _) => outcomes.Add(o)).Returns(Task.CompletedTask);
        Answers(new ChatMessageContent(AuthorRole.Assistant, "{}"));
        var scope = new LlmCallScope("org_A", LlmPipelines.Hierarchical, LlmOperations.QueryExpansion) { UserId = "auth0|ana" };

        await _service.Object.CompleteMeteredAsync("¿Ayudas?", Model, scope, _metrics.Object, recorder: recorder.Object);

        outcomes.Should().ContainSingle().Which.Should().Match<AiCallOutcome>(o =>
            o.TenantId == "org_A" && o.UserId == "auth0|ana" && o.ModelId == "gpt-4o-mini" && o.Provider == "openai"
            && o.Pipeline == LlmPipelines.Hierarchical && o.Operation == LlmOperations.QueryExpansion && o.Success);
        _recorded.Should().BeEmpty("the recorder emits the metrics");
    }

    [Fact]
    public async Task A_failed_call_for_a_user_is_stored_as_a_failure()
    {
        AiCallOutcome? outcome = null;
        var recorder = new Mock<IAiCallRecorder>();
        recorder.Setup(r => r.RecordAsync(It.IsAny<AiCallOutcome>(), It.IsAny<CancellationToken>()))
            .Callback<AiCallOutcome, CancellationToken>((o, _) => outcome = o).Returns(Task.CompletedTask);
        _service.Setup(s => s.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));
        var scope = new LlmCallScope("org_A", LlmPipelines.Profile, LlmOperations.HistoryAnalysis) { UserId = "auth0|ana" };

        var act = () => _service.Object.CompleteMeteredAsync(new ChatHistory(), Model, scope, _metrics.Object, recorder: recorder.Object);

        await act.Should().ThrowAsync<InvalidOperationException>();
        outcome!.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(LlmErrorCodes.Unavailable);
    }

    [Fact]
    public async Task Without_a_user_only_the_metrics_are_recorded_even_with_a_recorder()
    {
        var recorder = new Mock<IAiCallRecorder>();
        Answers(new ChatMessageContent(AuthorRole.Assistant, "pregunta"));

        await _service.Object.CompleteMeteredAsync("texto", Model, Scope, _metrics.Object, recorder: recorder.Object);

        recorder.Verify(r => r.RecordAsync(It.IsAny<AiCallOutcome>(), It.IsAny<CancellationToken>()), Times.Never);
        _recorded.Should().ContainSingle();
    }

    [Fact]
    public void Platform_models_come_from_the_kernel_settings()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Chat:Provider"] = "google",
            ["Ai:Chat:Model"] = "gemini-3.5-flash",
            ["Ai:Ingestion:Model"] = "gemini-3.5-flash-lite",
        }).Build();

        PlatformModels.Chat(configuration).Should().Be(("google", "gemini-3.5-flash"));
        PlatformModels.Ingestion(configuration).Should().Be(("google", "gemini-3.5-flash-lite"));
    }
}
