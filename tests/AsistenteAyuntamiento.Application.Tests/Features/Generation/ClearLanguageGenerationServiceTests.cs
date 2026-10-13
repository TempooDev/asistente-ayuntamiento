using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Domain.Common.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Generation;

/// <summary>
/// The hierarchical answer of a user is recorded in AiCallLogs like the baseline's, and each parent document is
/// labelled so the model can cite it in "Fuentes consultadas" (spec: hierarchical-retrieval).
/// </summary>
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
    public async Task A_stream_that_cannot_start_is_recorded_and_thrown_with_its_code()
    {
        _chat.Setup(s => s.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("provider down"));

        // Typed like the baseline, never a generic text the Arena would treat as an answer.
        var act = () => StreamAsync(new LlmCallScope("org_A", LlmPipelines.Hierarchical) { UserId = "auth0|ana" });

        (await act.Should().ThrowAsync<LlmStreamInterruptedException>()).Which.ErrorCode.Should().Be(LlmErrorCodes.Unavailable);
        _recorded.Should().ContainSingle().Which.Should().Match<AiCallOutcome>(o =>
            !o.Success && o.ErrorCode == LlmErrorCodes.Unavailable && o.ErrorDetail == "provider down");
    }

    [Fact]
    public async Task A_stream_failing_midway_is_thrown_with_its_code_after_the_text()
    {
        _chat.Setup(s => s.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .Returns(FailAfter("Tienes "));
        var received = new System.Text.StringBuilder();

        var act = async () =>
        {
            await foreach (var chunk in _sut.GenerateStreamingResponseAsync("¿Qué plazo hay?", Context, new LlmCallScope("org_A", LlmPipelines.Hierarchical)))
            {
                received.Append(chunk.Content);
            }
        };

        await act.Should().ThrowAsync<LlmStreamInterruptedException>();
        received.ToString().Should().Be("Tienes ");
    }

    [Fact]
    public async Task A_failed_non_streaming_answer_is_thrown_instead_of_returned_as_text()
    {
        _chat.Setup(s => s.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));

        var act = () => _sut.GenerateResponseAsync("¿Qué plazo hay?", Context, new LlmCallScope("org_A", LlmPipelines.Hierarchical));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static async IAsyncEnumerable<StreamingChatMessageContent> FailAfter(string text)
    {
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, text);
        await Task.Yield();
        throw new InvalidOperationException("connection reset");
    }

    [Fact]
    public async Task Without_a_user_only_the_metrics_are_recorded()
    {
        Streams("Respuesta");

        await StreamAsync(new LlmCallScope("org_A", LlmPipelines.Hierarchical));

        _recorded.Should().BeEmpty();
        _metrics.Verify(m => m.Record(It.Is<LlmCallMetrics>(c => c.TenantId == "org_A" && c.ErrorType == null)), Times.Once);
    }

    private static RetrievalResult Boe(long fragmentId = 1, long parentId = 10) => new(
        fragmentId, "fragmento", parentId, "Artículo 1. Se convocan ayudas.", 0.5,
        ParentDocumentId: "BOE-A-2026-1234",
        ParentTitle: "Orden de ayudas al alquiler",
        Bulletin: BulletinType.BOE,
        IssuingBody: "Ministerio de Vivienda",
        PublicationDate: new DateTime(2026, 10, 1));

    [Fact]
    public void A_BOE_document_is_labelled_with_title_bulletin_reference_and_public_url()
    {
        var label = ClearLanguageGenerationService.DocumentLabel(Boe());

        label.Should().Be("[Documento: Orden de ayudas al alquiler | Boletín: BOE | Referencia: BOE-A-2026-1234 | " +
            "Organismo: Ministerio de Vivienda | Fecha: 2026-10-01 | URL: https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1234]");
    }

    [Fact]
    public void A_BOJA_document_uses_the_same_public_url_as_the_baseline()
    {
        var label = ClearLanguageGenerationService.DocumentLabel(new RetrievalResult(1, "x", 2, "texto", 0.1,
            ParentDocumentId: "BOJA-2026-10-1", ParentTitle: "", Bulletin: BulletinType.BOJA));

        label.Should().Be("[Documento: BOJA-2026-10-1 | Boletín: BOJA | Referencia: BOJA-2026-10-1 | URL: https://www.juntadeandalucia.es/eboja.html]");
    }

    [Fact]
    public async Task The_prompt_labels_each_distinct_parent_once_instead_of_its_database_id()
    {
        ChatHistory? prompt = null;
        _chat.Setup(s => s.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .Callback<ChatHistory, PromptExecutionSettings?, Kernel?, CancellationToken>((history, _, _, _) => prompt = history)
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, "respuesta")]);
        await _sut.GenerateResponseAsync("¿Qué ayudas hay?", [Boe(fragmentId: 1), Boe(fragmentId: 2)],
            new LlmCallScope("tenant", LlmPipelines.Hierarchical));

        var system = prompt!.First().Content;
        system.Should().Contain("Referencia: BOE-A-2026-1234").And.NotContain("ID Documento Padre");
        system!.Split("--- INICIO DE DOCUMENTO LEGAL ---").Should().HaveCount(2, "the two fragments share one parent");
    }
}
