using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Moq;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ingestion;

/// <summary>
/// Fragments carry the real sub-section in the breadcrumb (or none) and inherit the document's municipality as its match
/// key, in the database and in the vector payload (spec: hierarchical-retrieval). In-memory database, fake ports.
/// </summary>
public class HierarchicalFragmentMetadataTests
{
    private sealed class FixedProcessor(IAppDbContext db, IFragmentEnrichmentService enrichment, IVectorStore vectors, Kernel kernel, string? municipality)
        : BaseHierarchicalIngestionProcessor(db, enrichment, Mock.Of<IIngestionMetricsService>(), vectors, NullLogger.Instance, kernel)
    {
        protected override BulletinType Bulletin => BulletinType.BOE;

        protected override Task<ParentDocument?> ExtractParentDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken) =>
            Task.FromResult<ParentDocument?>(new ParentDocument
            {
                Bulletin = BulletinType.BOE,
                DocumentId = documentId,
                NormTitle = "Ordenanza",
                IssuingBody = "Ayuntamiento",
                Municipality = municipality,
                FullText = "texto"
            });

        protected override Task<IEnumerable<RawFragment>> ExtractFragmentsAsync(string blobPath, string documentId, ParentDocument parentDoc, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<RawFragment>>(
            [
                new RawFragment { Section = "a1", OriginalText = "uno" },
                new RawFragment { Section = "a2", SubSection = "parte 1", OriginalText = "dos" },
            ]);
    }

    [Theory]
    [InlineData("Sedella", "sedella")]
    [InlineData("Vélez-Málaga", "velez malaga")]
    [InlineData(null, null)]
    public async Task Fragments_get_the_real_sub_section_and_the_document_municipality(string? municipality, string? key)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"fragments-{Guid.NewGuid()}").Options,
            Mock.Of<ICurrentTenantService>(t => t.TenantId == "system"));

        var enrichment = new Mock<IFragmentEnrichmentService>();
        enrichment.Setup(e => e.EnrichFragmentAsync(It.IsAny<BulletinType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulletinType b, string body, string norm, string section, string? sub, string text, CancellationToken _) =>
                (FragmentEnrichmentService.BuildBreadcrumb(b, body, norm, section, sub) + "\n" + text, 0, 0));

        IReadOnlyList<VectorPoint> upserted = [];
        var vectors = new Mock<IVectorStore>();
        vectors.Setup(v => v.UpsertAsync(VectorCollection.ChildFragments, It.IsAny<IReadOnlyList<VectorPoint>>(), It.IsAny<CancellationToken>()))
            .Callback<VectorCollection, IReadOnlyList<VectorPoint>, CancellationToken>((_, points, _) => upserted = points)
            .Returns(Task.CompletedTask);

        await new FixedProcessor(db, enrichment.Object, vectors.Object, KernelWithFakeEmbeddings(), municipality)
            .ProcessDocumentAsync("json/BOE/BOE-A-1.json", "BOE-A-1", CancellationToken.None);

        var children = await db.ChildFragments.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        children.Select(c => c.SubSection).Should().Equal("a1", "a2 (parte 1)");
        children.Should().OnlyContain(c => c.Municipality == key);
        (await db.ParentDocuments.AsNoTracking().SingleAsync()).Municipality.Should().Be(municipality, "the parent keeps the display name");
        children[0].ChunkText.Should().StartWith("[BOLETÍN: BOE | ORGANISMO: Ayuntamiento | NORMA: Ordenanza | ARTÍCULO: a1]");
        children[1].ChunkText.Should().StartWith("[BOLETÍN: BOE | ORGANISMO: Ayuntamiento | NORMA: Ordenanza | ARTÍCULO: a2 | SUBSECCIÓN: parte 1]");
        children.Should().NotContain(c => c.ChunkText.Contains("General") || c.ChunkText.Contains("Párrafo"));

        upserted.Should().HaveCount(2).And.OnlyContain(p => (string)p.Payload[VectorPayloadKeys.Municipality]! == (key ?? ""));
    }

    private static Kernel KernelWithFakeEmbeddings()
    {
        var generator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> values, EmbeddingGenerationOptions? _, CancellationToken _) =>
                new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(new float[] { 0.1f, 0.2f }))));
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(generator.Object);
        return builder.Build();
    }
}
