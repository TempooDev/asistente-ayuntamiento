using System.Text;
using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion;

/// <summary>
/// BOE fragments come from the raw XML articles when it exists and from the JSON text otherwise, since the scraper
/// stores the XML only as a best-effort backup (spec: hierarchical-retrieval).
/// </summary>
public class BoeFragmentationTests
{
    private const string DocumentId = "BOE-A-2026-1234";
    private const string JsonPath = $"json/BOE/{DocumentId}.json";
    private const string XmlPath = $"raw-xml/BOE/{DocumentId}.xml";

    private sealed class TestableBoe(IBlobStorage blobs, IChunkingStrategy chunking, Kernel kernel)
        : BoeIngestionService(blobs, chunking, Mock.Of<IAppDbContext>(), Mock.Of<IFragmentEnrichmentService>(),
            Mock.Of<IIngestionMetricsService>(), Mock.Of<IVectorStore>(), NullLogger<BoeIngestionService>.Instance, kernel)
    {
        public async Task<List<RawFragment>> Fragments()
        {
            var parent = await ExtractParentDocumentAsync(JsonPath, DocumentId, CancellationToken.None);
            return (await ExtractFragmentsAsync(JsonPath, DocumentId, parent!, CancellationToken.None)).ToList();
        }
    }

    private static TestableBoe Create(string jsonText, string? xml, IChunkingStrategy? chunking = null)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { document_id = DocumentId, text = jsonText, metadata = new { titulo = "Orden" } });
        var blobs = new Mock<IBlobStorage>();
        blobs.Setup(b => b.OpenReadAsync(JsonPath, It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(json));
        if (xml is null)
        {
            blobs.Setup(b => b.OpenReadAsync(XmlPath, It.IsAny<CancellationToken>())).ThrowsAsync(new BlobNotFoundException(XmlPath));
        }
        else
        {
            blobs.Setup(b => b.OpenReadAsync(XmlPath, It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        }

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(Mock.Of<IEmbeddingGenerator<string, Embedding<float>>>());
        return new TestableBoe(blobs.Object, chunking ?? new FixedOverlapChunkingStrategy(), kernelBuilder.Build());
    }

    [Fact]
    public async Task Without_the_raw_XML_the_JSON_text_is_used_as_a_single_article()
    {
        var fragments = await Create("Artículo único. Se aprueba la convocatoria.", xml: null).Fragments();

        fragments.Should().ContainSingle();
        fragments[0].Section.Should().Be("1");
        fragments[0].SubSection.Should().BeNull();
        fragments[0].OriginalText.Should().Contain("Se aprueba la convocatoria");
    }

    [Fact]
    public async Task With_the_raw_XML_each_article_is_a_section()
    {
        const string xml = "<documento><texto><articulo id=\"a1\">Primero.</articulo><articulo id=\"a2\">Segundo.</articulo></texto></documento>";

        var fragments = await Create("texto json", xml).Fragments();

        fragments.Select(f => (f.Section, f.SubSection, f.OriginalText)).Should().Equal(("a1", null, "Primero."), ("a2", null, "Segundo."));
    }

    [Fact]
    public async Task A_split_article_names_each_part_as_its_sub_section()
    {
        var longArticle = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => string.Join(' ', Enumerable.Repeat($"palabra{i}", 25))));
        var xml = $"<documento><texto><articulo id=\"a5\">{longArticle}</articulo></texto></documento>";

        var fragments = await Create("x", xml, new FixedOverlapChunkingStrategy(maxTokens: 40, overlapTokens: 0)).Fragments();

        fragments.Should().HaveCountGreaterThan(1).And.OnlyContain(f => f.Section == "a5");
        fragments.Select(f => f.SubSection).Should().StartWith("parte 1");
        fragments.Select(f => f.Label).Should().StartWith("a5 (parte 1)").And.OnlyHaveUniqueItems();
    }
}
