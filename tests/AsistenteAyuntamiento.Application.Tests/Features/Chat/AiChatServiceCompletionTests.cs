using System.Net;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Application.Features.Users;
using AsistenteAyuntamiento.Domain.Features.Chat;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>
/// Non-streaming completion: embeddings source, quota fallback and error codes
/// (specs: llm-provider-security; audit tasks 2.1 and 2.2).
/// </summary>
public class AiChatServiceCompletionTests
{
    private const string TenantKey = "tenant-secret-key";

    private readonly Mock<IChatModelFactory> _modelFactory = new();
    private readonly Mock<IChatCompletionService> _primary = new();
    private readonly CountingEmbeddingGenerator _embeddings = new();

    private AiChatService CreateSut()
    {
        var configService = new Mock<IAiConfigurationService>();
        configService
            .Setup(s => s.GetFullConfigurationAsync(It.IsAny<string?>()))
            .ReturnsAsync((new AiConfigurationDto { Provider = "openai", Model = "tenant-model" }, (string?)TenantKey));

        var preferences = new Mock<IUserPreferenceService>();
        preferences
            .Setup(s => s.GetPreferencesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserPreferenceDto());

        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.AiCallLogs).Returns(new Mock<DbSet<AiCallLog>>().Object);

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_embeddings);

        _modelFactory.Setup(f => f.Create(It.IsAny<AiConfigurationDto>(), It.IsAny<string?>())).Returns(_primary.Object);

        return new AiChatService(
            configService.Object,
            Mock.Of<IAiMetricsService>(),
            NullLogger<AiChatService>.Instance,
            db.Object,
            kernelBuilder.Build(),
            Mock.Of<IHybridRetrievalService>(),
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IClearLanguageGenerationService>(),
            preferences.Object,
            EmptyVectorStore(),
            _modelFactory.Object);
    }

    private static ChatHistory History()
    {
        var history = new ChatHistory();
        history.AddUserMessage("¿Qué ayudas hay para el alquiler?");
        return history;
    }

    private static void Replies(Mock<IChatCompletionService> service, string content) =>
        service
            .Setup(s => s.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, content)]);

    private static void Throws(Mock<IChatCompletionService> service, Exception exception) =>
        service
            .Setup(s => s.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

    [Fact]
    public async Task Completion_with_a_user_message_uses_the_platform_embedding_generator()
    {
        Replies(_primary, "respuesta");

        var result = await CreateSut().GetCompletionAsync(History(), "org_A", "u1");

        result.Success.Should().BeTrue();
        result.Content.Should().Be("respuesta");
        _embeddings.Calls.Should().Be(1);
        _modelFactory.Verify(f => f.Create(It.IsAny<AiConfigurationDto>(), TenantKey), Times.Once);
    }

    [Fact]
    public async Task Quota_exceeded_with_fallback_configured_answers_from_the_fallback()
    {
        Throws(_primary, new HttpOperationException(HttpStatusCode.TooManyRequests, null, "Too Many Requests", null));
        var fallback = new Mock<IChatCompletionService>();
        Replies(fallback, "respuesta de respaldo");
        _modelFactory.Setup(f => f.CreateFallback()).Returns(new FallbackChatModel("fallback-model", fallback.Object));

        var result = await CreateSut().GetCompletionAsync(History(), "org_A", "u1");

        result.Success.Should().BeTrue();
        result.Content.Should().StartWith("respuesta de respaldo");
        fallback.Verify(s => s.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Quota_exceeded_without_fallback_returns_the_quota_error_code()
    {
        Throws(_primary, new HttpOperationException(HttpStatusCode.TooManyRequests, null, "Too Many Requests", null));
        _modelFactory.Setup(f => f.CreateFallback()).Returns((FallbackChatModel?)null);

        var result = await CreateSut().GetCompletionAsync(History(), "org_A", "u1");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(LlmErrorCodes.QuotaExceeded);
        result.Content.Should().Be(LlmErrorCodes.UserMessage(LlmErrorCodes.QuotaExceeded));
    }

    [Fact]
    public async Task Non_quota_error_mentioning_limit_does_not_use_the_fallback_nor_leak_details()
    {
        Throws(_primary, new InvalidOperationException("context length limit exceeded for model tenant-model"));

        var result = await CreateSut().GetCompletionAsync(History(), "org_A", "u1");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(LlmErrorCodes.Unavailable);
        result.Content.Should().NotContain("context length");
        _modelFactory.Verify(f => f.CreateFallback(), Times.Never);
    }

    [Fact]
    public async Task Missing_api_key_returns_the_not_configured_error_code()
    {
        var sut = CreateSut();
        _modelFactory.Setup(f => f.Create(It.IsAny<AiConfigurationDto>(), It.IsAny<string?>()))
            .Throws(new LlmNotConfiguredException("No API key configured for provider 'openai'."));

        var result = await sut.GetCompletionAsync(History(), "org_A", "u1");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(LlmErrorCodes.NotConfigured);
    }

    private sealed class CountingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int Calls { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var embeddings = values.Select(_ => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f }));
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    /// <summary>Vector store with no matches.</summary>
    private static IVectorStore EmptyVectorStore()
    {
        var store = new Mock<IVectorStore>();
        store
            .Setup(v => v.SearchAsync(It.IsAny<VectorCollection>(), It.IsAny<VectorSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VectorSearchHit>());
        return store.Object;
    }
}
