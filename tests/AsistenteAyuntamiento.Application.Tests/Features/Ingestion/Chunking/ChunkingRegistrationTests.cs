using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Domain.Common.Enums;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion.Chunking;

/// <summary>The three strategies are keyed singletons; the unkeyed one is the fixed strategy.</summary>
public class ChunkingRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        var services = new ServiceCollection().AddApplicationServices(configuration);
        services.AddKernel();
        services.AddSingleton(Mock.Of<IEmbeddingGenerator<string, Embedding<float>>>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Each_strategy_is_registered_under_its_name()
    {
        using var provider = Build();

        provider.GetRequiredService<IChunkingStrategy>().Should().BeOfType<FixedOverlapChunkingStrategy>()
            .And.BeSameAs(provider.GetRequiredKeyedService<IChunkingStrategy>(ChunkingStrategies.FixedOverlap));
        provider.GetRequiredKeyedService<IChunkingStrategy>(ChunkingStrategies.Recursive).Should().BeOfType<RecursiveChunkingStrategy>();
        provider.GetRequiredKeyedService<IChunkingStrategy>(ChunkingStrategies.Semantic).Should().BeOfType<SemanticChunkingStrategy>();
        FlatChunkVariant.All.Select(v => provider.GetRequiredKeyedService<IChunkingStrategy>(v.ChunkingStrategy).StrategyName)
            .Should().Equal(FlatChunkVariant.All.Select(v => v.ChunkingStrategy));
    }

    [Fact]
    public async Task The_recursive_strategy_falls_back_to_the_embedding_limits()
    {
        using var defaults = Build(new() { ["Ai:Embeddings:ChunkMaxTokens"] = "20", ["Ai:Embeddings:ChunkOverlapTokens"] = "0" });
        using var own = Build(new()
        {
            ["Ai:Embeddings:ChunkMaxTokens"] = "20",
            ["Ai:Embeddings:ChunkOverlapTokens"] = "0",
            ["Ai:Chunking:Recursive:ChunkMaxTokens"] = "40",
        });
        var text = string.Join(" ", Enumerable.Range(1, 40).Select(i => $"palabra{i}"));

        var withDefaults = await defaults.GetRequiredKeyedService<IChunkingStrategy>(ChunkingStrategies.Recursive).ChunkTextAsync(text);
        var withOwn = await own.GetRequiredKeyedService<IChunkingStrategy>(ChunkingStrategies.Recursive).ChunkTextAsync(text);

        withDefaults.Should().OnlyContain(c => ChunkTokenCounter.Count(c.Text) <= 20);
        withOwn.Should().HaveCountLessThan(withDefaults.Count);
    }

    [Theory]
    [InlineData("Ai:Chunking:Recursive:ChunkOverlapTokens", "400")]
    [InlineData("Ai:Chunking:Recursive:ChunkMaxTokens", "0")]
    [InlineData("Ai:Chunking:Semantic:BreakpointPercentile", "0")]
    [InlineData("Ai:Chunking:Semantic:MinTokens", "500")]
    [InlineData("Ai:Chunking:Semantic:BufferSize", "-1")]
    public void Invalid_study_settings_fail_at_registration(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();

        FluentActions.Invoking(() => new ServiceCollection().AddApplicationServices(configuration)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Each_flat_variant_has_its_own_ingestion_service_and_the_unkeyed_one_is_the_baseline()
    {
        var services = new ServiceCollection().AddApplicationServices(new ConfigurationBuilder().Build());

        foreach (var pipeline in new[] { PipelineType.Baseline, PipelineType.Recursive, PipelineType.Semantic })
        {
            services.Should().Contain(d => d.ServiceType == typeof(IDocumentIngestionService) && Equals(d.ServiceKey, pipeline));
        }
        services.Should().NotContain(d => d.ServiceType == typeof(IDocumentIngestionService) && Equals(d.ServiceKey, PipelineType.Hierarchical));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IDocumentIngestionService) && d.ServiceKey == null);
    }
}
