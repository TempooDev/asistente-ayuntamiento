using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>Ingestion cost of the corpus per pipeline, from <c>IngestionMetrics</c> and the collection sizes.</summary>
public sealed class IndexingReporter(IAppDbContext dbContext, IVectorStore vectorStore)
{
    public async Task<IndexingReport> BuildAsync(IReadOnlyList<PipelineType> pipelines, IReadOnlyList<string> corpus, CancellationToken cancellationToken = default)
    {
        var result = new List<PipelineIndexing>();
        foreach (var pipeline in pipelines)
        {
            var metrics = await dbContext.IngestionMetrics.AsNoTracking()
                .Where(m => m.Pipeline == pipeline && corpus.Contains(m.DocumentId))
                .ToListAsync(cancellationToken);

            // A re-ingested document has several rows: the latest one describes what is in the collection now.
            var documents = metrics.GroupBy(m => m.DocumentId)
                .Select(g => g.MaxBy(m => m.CreatedAt)!)
                .OrderBy(m => m.DocumentId, StringComparer.Ordinal)
                .Select(m => new DocumentIndexing(m.DocumentId, m.ProcessingDurationMs, m.ChunksGenerated, m.TotalTokensEmbedded, m.TotalLlmCalls, m.TotalLlmTokens))
                .ToList();

            var collection = EvaluationVariants.Collections[pipeline];
            var stats = await vectorStore.GetStatsAsync(collection, cancellationToken);
            result.Add(new PipelineIndexing(pipeline, collection.ToString(), stats?.Points ?? 0, documents));
        }

        return new IndexingReport(result);
    }
}
