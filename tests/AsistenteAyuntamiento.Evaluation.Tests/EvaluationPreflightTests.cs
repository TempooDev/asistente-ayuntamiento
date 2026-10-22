using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Domain.Common.Enums;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;

namespace AsistenteAyuntamiento.Evaluation.Tests;

public class EvaluationPreflightTests
{
    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddings = new();

    public EvaluationPreflightTests()
    {
        _embeddings.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[1024])]));
        _vectorStore.Setup(v => v.CountDocumentPointsAsync(It.IsAny<VectorCollection>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(3);
    }

    private Task<IReadOnlyList<string>> Check(params PipelineType[] pipelines) =>
        new EvaluationPreflight(_vectorStore.Object, _embeddings.Object).CheckAsync(pipelines, ["BOE-A-2026-1", "BOJA-2026-1-1"]);

    [Fact]
    public async Task Passes_when_every_collection_has_the_model_size_and_all_documents()
    {
        _vectorStore.Setup(v => v.GetStatsAsync(It.IsAny<VectorCollection>(), It.IsAny<CancellationToken>())).ReturnsAsync(new VectorCollectionStats(1024, 50));

        (await Check(PipelineType.Baseline, PipelineType.Hierarchical)).Should().BeEmpty();
    }

    [Fact]
    public async Task Names_the_collection_and_both_sizes_on_a_mismatch()
    {
        _vectorStore.Setup(v => v.GetStatsAsync(VectorCollection.ChildFragments, It.IsAny<CancellationToken>())).ReturnsAsync(new VectorCollectionStats(768, 50));

        (await Check(PipelineType.Hierarchical)).Should().ContainSingle()
            .Which.Should().Contain("ChildFragments").And.Contain("768").And.Contain("1024");
    }

    [Fact]
    public async Task Lists_the_variant_and_the_documents_without_points()
    {
        _vectorStore.Setup(v => v.GetStatsAsync(VectorCollection.DocumentChunks, It.IsAny<CancellationToken>())).ReturnsAsync(new VectorCollectionStats(1024, 3));
        _vectorStore.Setup(v => v.CountDocumentPointsAsync(VectorCollection.DocumentChunks, "BOJA-2026-1-1", It.IsAny<CancellationToken>())).ReturnsAsync(0);

        (await Check(PipelineType.Baseline)).Should().ContainSingle()
            .Which.Should().StartWith("Baseline").And.Contain("BOJA-2026-1-1").And.NotContain("BOE-A-2026-1");
    }

    [Fact]
    public async Task Reports_a_missing_collection()
    {
        (await Check(PipelineType.Baseline)).Should().ContainSingle().Which.Should().Contain("does not exist");
    }
}
