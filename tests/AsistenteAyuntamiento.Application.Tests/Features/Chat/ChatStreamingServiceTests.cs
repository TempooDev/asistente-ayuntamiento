using System.Net;
using System.Runtime.CompilerServices;
using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using AsistenteAyuntamiento.Application.Features.Chat;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>Streaming answers: quota fallback, typed errors and usage recording (spec: llm-provider-security).</summary>
public class ChatStreamingServiceTests
{
    private readonly Mock<IChatModelFactory> _modelFactory = new();
    private readonly Mock<IChatCompletionService> _primary = new();
    private readonly List<AiCallOutcome> _recorded = [];

    private ChatStreamingService CreateSut()
    {
        var config = new Mock<IAiConfigurationService>();
        config.Setup(c => c.GetFullConfigurationAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AiConfigurationDto { Provider = "openai", Model = "tenant-model" }, (string?)"key"));

        var recorder = new Mock<IAiCallRecorder>();
        recorder.Setup(r => r.RecordAsync(It.IsAny<AiCallOutcome>(), It.IsAny<CancellationToken>()))
            .Callback<AiCallOutcome, CancellationToken>((outcome, _) => _recorded.Add(outcome))
            .Returns(Task.CompletedTask);

        _modelFactory.Setup(f => f.Create(It.IsAny<AiConfigurationDto>(), It.IsAny<string?>())).Returns(_primary.Object);
        return new ChatStreamingService(config.Object, _modelFactory.Object, recorder.Object, NullLogger<ChatStreamingService>.Instance);
    }

    private static ChatHistory History()
    {
        var history = new ChatHistory();
        history.AddUserMessage("¿Qué ayudas hay?");
        return history;
    }

    private static HttpOperationException QuotaExceeded() => new(HttpStatusCode.TooManyRequests, null, "Too Many Requests", null);

    private static void Streams(Mock<IChatCompletionService> service, Func<IAsyncEnumerable<StreamingChatMessageContent>> stream) =>
        service
            .Setup(s => s.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .Returns(stream);

    private static async IAsyncEnumerable<StreamingChatMessageContent> Chunks(params string[] texts)
    {
        foreach (var text in texts)
        {
            await Task.Yield();
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, text);
        }
    }

    private static async IAsyncEnumerable<StreamingChatMessageContent> ChunksThenThrow(Exception error, params string[] texts)
    {
        await foreach (var chunk in Chunks(texts))
        {
            yield return chunk;
        }
        throw error;
    }

    private async Task<List<string>> CollectAsync() => await CreateSut().StreamAsync(History(), "org_A", "u1").ToListAsync();

    [Fact]
    public async Task Streams_the_model_chunks_and_records_a_successful_call()
    {
        Streams(_primary, () => Chunks("Hola", ", ", "mundo"));

        var chunks = await CollectAsync();

        chunks.Should().Equal("Hola", ", ", "mundo");
        _recorded.Should().ContainSingle().Which.Should().Match<AiCallOutcome>(o =>
            o.Success && o.ModelId == "tenant-model" && o.ResponseLength == "Hola, mundo".Length && o.ErrorCode == null);
    }

    [Fact]
    public async Task Quota_exceeded_before_the_first_chunk_switches_to_the_fallback_model()
    {
        Streams(_primary, () => ChunksThenThrow(QuotaExceeded()));
        var fallback = new Mock<IChatCompletionService>();
        Streams(fallback, () => Chunks("respaldo"));
        _modelFactory.Setup(f => f.CreateFallback()).Returns(new FallbackChatModel("fallback-model", fallback.Object));

        var chunks = await CollectAsync();

        chunks.Should().HaveCount(2);
        chunks[0].Should().Contain("modelo de respaldo");
        chunks[1].Should().Be("respaldo");
        _recorded.Single().ModelId.Should().Be("fallback-model");
    }

    [Fact]
    public async Task Quota_exceeded_without_fallback_streams_the_quota_message()
    {
        Streams(_primary, () => ChunksThenThrow(QuotaExceeded()));
        _modelFactory.Setup(f => f.CreateFallback()).Returns((FallbackChatModel?)null);

        var chunks = await CollectAsync();

        chunks.Should().Equal(LlmErrorCodes.UserMessage(LlmErrorCodes.QuotaExceeded));
        _recorded.Single().Should().Match<AiCallOutcome>(o => !o.Success && o.ErrorCode == LlmErrorCodes.QuotaExceeded);
    }

    [Fact]
    public async Task A_failure_after_content_keeps_the_partial_answer_without_an_error_message()
    {
        Streams(_primary, () => ChunksThenThrow(new IOException("connection reset"), "Parte"));

        var chunks = await CollectAsync();

        chunks.Should().Equal("Parte");
        _modelFactory.Verify(f => f.CreateFallback(), Times.Never);
    }

    [Fact]
    public async Task A_failure_before_content_streams_the_typed_error_without_provider_details()
    {
        Streams(_primary, () => ChunksThenThrow(new InvalidOperationException("internal provider detail")));

        var chunks = await CollectAsync();

        chunks.Should().ContainSingle().Which.Should().Contain(LlmErrorCodes.UserMessage(LlmErrorCodes.Unavailable)).And.NotContain("provider detail");
        _recorded.Single().ErrorCode.Should().Be(LlmErrorCodes.Unavailable);
    }

    [Fact]
    public async Task Missing_api_key_streams_the_not_configured_message_and_records_nothing()
    {
        var sut = CreateSut();
        _modelFactory.Setup(f => f.Create(It.IsAny<AiConfigurationDto>(), It.IsAny<string?>()))
            .Throws(new LlmNotConfiguredException("No API key"));

        var chunks = await sut.StreamAsync(History(), "org_A", "u1").ToListAsync();

        chunks.Should().Equal(LlmErrorCodes.UserMessage(LlmErrorCodes.NotConfigured));
        _recorded.Should().BeEmpty();
    }
}
