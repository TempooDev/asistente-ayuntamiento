using System.Text;
using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion;

/// <summary>BOJA fragments follow the configured chunking strategy instead of fixed 1000-character slices.</summary>
public class BojaFragmentationTests
{
    private sealed class TestableBoja(IBlobStorage blobs, IChunkingStrategy chunking, Kernel kernel)
        : BojaIngestionService(blobs, chunking, Mock.Of<IAppDbContext>(), Mock.Of<IFragmentEnrichmentService>(),
            Mock.Of<IIngestionMetricsService>(), Mock.Of<IVectorStore>(), NullLogger<BojaIngestionService>.Instance, kernel)
    {
        public Task<IEnumerable<RawFragment>> Fragments(string blobPath) =>
            ExtractFragmentsAsync(blobPath, "BOJA-2026-1-1", new ParentDocument(), CancellationToken.None);
    }

    private static TestableBoja Create(string text, IChunkingStrategy chunking)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { document_id = "BOJA-2026-1-1", text, metadata = new { } });
        var blobs = new Mock<IBlobStorage>();
        blobs.Setup(b => b.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(json));

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(Mock.Of<IEmbeddingGenerator<string, Embedding<float>>>());
        return new TestableBoja(blobs.Object, chunking, kernelBuilder.Build());
    }

    [Fact]
    public async Task Splits_with_the_chunking_strategy_and_never_cuts_words()
    {
        var paragraphs = Enumerable.Range(1, 30).Select(i => $"Artículo {i}. La Consejería resuelve conceder la ayuda número {i} a la entidad solicitante.");
        var text = string.Join("\n\n", paragraphs);

        var fragments = (await Create(text, new FixedOverlapChunkingStrategy(maxTokens: 60, overlapTokens: 10)).Fragments("json/BOJA/x.json")).ToList();

        fragments.Should().HaveCountGreaterThan(1);
        fragments.Select(f => f.Section).Should().StartWith("Sección 1").And.OnlyHaveUniqueItems();
        fragments.Should().OnlyContain(f => f.ElementType == "Párrafo");
        // Every fragment starts and ends on a word boundary (the old slices cut every 1000 characters).
        var words = Words(text).ToHashSet();
        foreach (var fragment in fragments)
        {
            var fragmentWords = Words(fragment.OriginalText);
            words.Should().Contain(fragmentWords.First()).And.Contain(fragmentWords.Last());
        }
    }

    private static string[] Words(string text) => text.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task An_empty_document_has_no_fragments()
    {
        var fragments = await Create("", new FixedOverlapChunkingStrategy()).Fragments("json/BOJA/empty.json");

        fragments.Should().BeEmpty();
    }
}
