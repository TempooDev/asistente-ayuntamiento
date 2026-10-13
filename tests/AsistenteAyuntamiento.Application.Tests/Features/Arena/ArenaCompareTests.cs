using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Arena.Models;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Domain.Features.Arena;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Arena;

/// <summary>Arena page comparison: a side whose model failed is not a vote option, so nothing is saved.</summary>
public class ArenaCompareTests
{
    private readonly Mock<IAppDbContext> _db = new();
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddings = new();
    private readonly Mock<IClearLanguageGenerationService> _generation = new();
    private readonly Mock<IChatCompletionService> _chat = new();
    private readonly Mock<IVectorStore> _vectorStore = new();

    public ArenaCompareTests()
    {
        _db.Setup(d => d.ArenaBattles).Returns(new List<ArenaBattle>().BuildMockDbSet().Object);
        _generation.Setup(g => g.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<RetrievalResult>>(), It.IsAny<LlmCallScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Respuesta jerárquica");
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<VectorCollection>(), It.IsAny<VectorSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private ArenaService Service()
    {
        var kernel = Kernel.CreateBuilder();
        kernel.Services.AddSingleton(_chat.Object);
        kernel.Services.AddSingleton(_embeddings.Object);

        var expansion = new Mock<IQueryExpansionService>();
        expansion.Setup(e => e.ExpandQueryAsync(It.IsAny<string>(), It.IsAny<LlmCallScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpandedQueryInfo());
        var retrieval = new Mock<IHybridRetrievalService>();
        retrieval.Setup(r => r.RetrieveAsync(It.IsAny<ExpandedQueryInfo>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var services = new ServiceCollection()
            .AddSingleton(expansion.Object)
            .AddSingleton(retrieval.Object)
            .AddSingleton(_generation.Object)
            .BuildServiceProvider();

        return new ArenaService(_db.Object, services.GetRequiredService<IServiceScopeFactory>(), kernel.Build(), _vectorStore.Object,
            new ConfigurationBuilder().Build(), Mock.Of<ILlmUsageMetrics>(), Mock.Of<IAiCallRecorder>(), NullLogger<ArenaService>.Instance);
    }

    [Fact]
    public async Task A_failed_baseline_side_fails_the_comparison_and_saves_no_battle()
    {
        _embeddings.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("429 Too Many Requests", null, System.Net.HttpStatusCode.TooManyRequests));

        var act = () => Service().CompareAsync(new ArenaCompareRequest { Query = "¿Plazos?" }, "org_A", "auth0|ana");

        (await act.Should().ThrowAsync<ArenaComparisonFailedException>()).Which.ErrorCode.Should().Be(LlmErrorCodes.QuotaExceeded);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_failed_hierarchical_side_fails_the_comparison_and_saves_no_battle()
    {
        // The baseline side answers normally.
        _embeddings.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[] { 0.1f, 0.2f })]));
        _chat.Setup(c => c.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, "Respuesta estándar")]);
        _generation.Setup(g => g.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<RetrievalResult>>(), It.IsAny<LlmCallScope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));

        var act = () => Service().CompareAsync(new ArenaCompareRequest { Query = "¿Plazos?" }, "org_A", "auth0|ana");

        (await act.Should().ThrowAsync<ArenaComparisonFailedException>()).Which.Message.Should().NotContain("provider down");
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task When_both_sides_answer_the_battle_is_saved()
    {
        _embeddings.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[] { 0.1f, 0.2f })]));
        _chat.Setup(c => c.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, "Respuesta estándar")]);

        var response = await Service().CompareAsync(new ArenaCompareRequest { Query = "¿Plazos?" }, "org_A", "auth0|ana");

        new[] { response.OptionAlfa, response.OptionBeta }.Should().BeEquivalentTo("Respuesta estándar", "Respuesta jerárquica");
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
