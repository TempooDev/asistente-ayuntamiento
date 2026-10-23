using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace AsistenteAyuntamiento.Evaluation.Tests;

/// <summary>The indexing cost is reported per variant, each from its own metrics and collection.</summary>
public class IndexingReporterTests
{
    [Fact]
    public async Task Each_variant_reports_its_own_metrics_and_collection()
    {
        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.IngestionMetrics).Returns(new List<IngestionMetric>
        {
            new() { Pipeline = PipelineType.Baseline, DocumentId = "BOE-A-2026-1", ChunksGenerated = 3, TotalTokensEmbedded = 900, CreatedAt = DateTime.UtcNow },
            new() { Pipeline = PipelineType.Recursive, DocumentId = "BOE-A-2026-1", ChunksGenerated = 4, TotalTokensEmbedded = 950, CreatedAt = DateTime.UtcNow },
            new() { Pipeline = PipelineType.Semantic, DocumentId = "BOE-A-2026-1", ChunksGenerated = 2, TotalTokensEmbedded = 3100, CreatedAt = DateTime.UtcNow },
        }.BuildMockDbSet().Object);
        var vectorStore = new Mock<IVectorStore>();
        vectorStore.Setup(v => v.GetStatsAsync(VectorCollection.RecursiveChunks, It.IsAny<CancellationToken>())).ReturnsAsync(new VectorCollectionStats(1024, 4));
        vectorStore.Setup(v => v.GetStatsAsync(VectorCollection.SemanticChunks, It.IsAny<CancellationToken>())).ReturnsAsync(new VectorCollectionStats(1024, 2));

        var report = await new IndexingReporter(db.Object, vectorStore.Object)
            .BuildAsync([PipelineType.Recursive, PipelineType.Semantic], ["BOE-A-2026-1"]);

        report.Pipelines.Select(p => (p.Pipeline, p.Collection, p.Points)).Should().Equal(
            (PipelineType.Recursive, nameof(VectorCollection.RecursiveChunks), 4L),
            (PipelineType.Semantic, nameof(VectorCollection.SemanticChunks), 2L));
        report.Pipelines.Select(p => p.Documents.Single().EmbeddedTokens).Should().Equal(950, 3100);
    }
}
