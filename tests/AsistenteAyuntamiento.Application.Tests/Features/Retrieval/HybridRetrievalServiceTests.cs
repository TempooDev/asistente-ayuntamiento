using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Search;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using MockQueryable.Moq;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Retrieval;

/// <summary>Hybrid retrieval runs on fakes of the ports: no Postgres, Qdrant or network (spec: clean-architecture-boundaries).</summary>
public class HybridRetrievalServiceTests
{
    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<IFullTextSearch> _fullText = new();

    [Fact]
    public void Fuse_rewards_ids_found_by_both_searches()
    {
        var fused = HybridRetrievalService.Fuse(denseRanking: [1, 2, 3], sparseRanking: [3, 4], limit: 10);

        fused.Select(f => f.Id).Should().Equal(3, 1, 2, 4);
        fused[0].Score.Should().BeApproximately(1.0 / 63 + 1.0 / 61, 1e-12);
    }

    [Fact]
    public void Fuse_keeps_dense_order_on_ties_and_applies_the_limit()
    {
        var fused = HybridRetrievalService.Fuse(denseRanking: [7], sparseRanking: [8], limit: 1);

        fused.Should().ContainSingle().Which.Id.Should().Be(7);
    }

    private HybridRetrievalService CreateSut(params ChildFragment[] fragments)
    {
        var embeddings = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        embeddings.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[] { 1f })]));
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(embeddings.Object);

        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ChildFragments).Returns(fragments.ToList().BuildMockDbSet().Object);

        return new HybridRetrievalService(db.Object, kernelBuilder.Build(), _vectorStore.Object, _fullText.Object, NullLogger<HybridRetrievalService>.Instance);
    }

    private static ChildFragment Fragment(long id) => new()
    {
        Id = id,
        ChunkText = $"fragmento {id}",
        Parent = new ParentDocument
        {
            Id = id * 100,
            DocumentId = $"BOE-A-2026-{id}",
            NormTitle = $"Orden {id}",
            Bulletin = BulletinType.BOE,
            IssuingBody = "Ministerio",
            FullText = $"artículo {id}"
        }
    };

    private static VectorSearchHit Hit(long id) => new(id, 0.9f, new Dictionary<string, object?>());

    [Fact]
    public async Task Returns_fused_fragments_with_their_parent_text_in_rank_order()
    {
        _vectorStore.Setup(v => v.SearchAsync(VectorCollection.ChildFragments, It.IsAny<VectorSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Hit(1), Hit(2)]);
        _fullText.Setup(f => f.SearchChildFragmentsAsync("vivienda", null, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([2L, 3L]);

        var results = await CreateSut(Fragment(1), Fragment(2), Fragment(3))
            .RetrieveAsync(new ExpandedQueryInfo { LexicalQuery = "vivienda", SemanticQuery = "ayudas a la vivienda" });

        results.Select(r => r.FragmentId).Should().Equal(2, 1, 3);
        results[0].ParentFullText.Should().Be("artículo 2");
        // The parent's citation data travels with the result, for "Fuentes consultadas".
        (results[0].ParentDocumentId, results[0].ParentTitle, results[0].Bulletin, results[0].IssuingBody)
            .Should().Be(("BOE-A-2026-2", "Orden 2", (BulletinType?)BulletinType.BOE, "Ministerio"));
    }

    [Fact]
    public async Task The_municipality_filters_both_searches_with_its_match_key()
    {
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<VectorCollection>(), It.IsAny<VectorSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _fullText.Setup(f => f.SearchChildFragmentsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // The model may answer with the Ayuntamiento, capitals or accents: fragments store the key "velez malaga".
        await CreateSut().RetrieveAsync(new ExpandedQueryInfo { LexicalQuery = "obras", SemanticQuery = "obras", MunicipalityFilter = "Ayuntamiento de VÉLEZ-Málaga" });

        _vectorStore.Verify(v => v.SearchAsync(VectorCollection.ChildFragments,
            It.Is<VectorSearchRequest>(r => r.TextFilter.HasValue && r.TextFilter.Value.Field == "Municipality" && r.TextFilter.Value.Text == "velez malaga"), It.IsAny<CancellationToken>()));
        _fullText.Verify(f => f.SearchChildFragmentsAsync("obras", "velez malaga", It.IsAny<int>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_failed_vector_search_still_returns_full_text_matches()
    {
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<VectorCollection>(), It.IsAny<VectorSearchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("qdrant down"));
        _fullText.Setup(f => f.SearchChildFragmentsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([3L]);

        var results = await CreateSut(Fragment(3)).RetrieveAsync(new ExpandedQueryInfo { LexicalQuery = "x", SemanticQuery = "x" });

        results.Should().ContainSingle().Which.FragmentId.Should().Be(3);
    }
}
