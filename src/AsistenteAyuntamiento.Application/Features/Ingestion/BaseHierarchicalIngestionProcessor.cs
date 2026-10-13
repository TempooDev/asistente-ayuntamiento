using AsistenteAyuntamiento.Domain.Common.Enums;
using System.Diagnostics;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.AI;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Microsoft.EntityFrameworkCore;
using static Qdrant.Client.Grpc.Conditions;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class RawFragment
{
    public string Section { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
}

public abstract class BaseHierarchicalIngestionProcessor : IHierarchicalIngestionProcessor
{
    protected readonly IAppDbContext _dbContext;
    protected readonly IFragmentEnrichmentService _enrichmentService;
    protected readonly IIngestionMetricsService _metricsService;
    protected readonly QdrantClient _qdrantClient;
    protected readonly ILogger _logger;
    protected readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService;
    protected readonly IVectorCollections _vectorCollections;

    protected BaseHierarchicalIngestionProcessor(
        IAppDbContext dbContext,
        IFragmentEnrichmentService enrichmentService,
        IIngestionMetricsService metricsService,
        QdrantClient qdrantClient,
        ILogger logger,
        Kernel kernel,
        IVectorCollections vectorCollections)
    {
        _dbContext = dbContext;
        _enrichmentService = enrichmentService;
        _metricsService = metricsService;
        _qdrantClient = qdrantClient;
        _logger = logger;
        _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        _vectorCollections = vectorCollections;
    }

    protected abstract BulletinType Bulletin { get; }

    protected abstract Task<ParentDocument?> ExtractParentDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken);

    protected abstract Task<IEnumerable<RawFragment>> ExtractFragmentsAsync(string blobPath, string documentId, ParentDocument parentDoc, CancellationToken cancellationToken);

    /// <summary>
    /// Idempotent per <c>documentId</c>:
    /// 1. LLM work (enrichment, embeddings) runs first and writes nothing.
    /// 2. PostgreSQL replaces any previous version of the document in one transaction (unique DocumentId).
    /// 3. After the commit, the vectors of previous versions are deleted and the new ones upserted.
    /// </summary>
    public async Task ProcessDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken)
    {
        if (Bulletin == BulletinType.BOPMA)
        {
            _logger.LogWarning("BOPMA document ingestion is discarded. Skipping {DocumentId}.", documentId);
            return;
        }

        var sw = Stopwatch.StartNew();
        int totalLlmCalls = 0;
        int totalLlmTokens = 0;
        int totalTokensEmbedded = 0;

        try
        {
            var parentDoc = await ExtractParentDocumentAsync(blobPath, documentId, cancellationToken);
            if (parentDoc == null) return;

            var fragments = (await ExtractFragmentsAsync(blobPath, documentId, parentDoc, cancellationToken)).ToList();

            // 1. Enrichment + embeddings (no side effects).
            var children = new List<ChildFragment>();
            int current = 0;
            foreach (var frag in fragments)
            {
                current++;
                if (current % 10 == 0 || current == fragments.Count)
                {
                    _logger.LogInformation("[{Bulletin} {DocumentId}] Procesando fragmento {Current}/{Total} de la sección '{Section}'", Bulletin, documentId, current, fragments.Count, frag.Section);
                }

                var enrichmentResult = await _enrichmentService.EnrichFragmentAsync(
                    Bulletin,
                    parentDoc.IssuingBody ?? "Estado",
                    parentDoc.NormTitle,
                    frag.Section,
                    frag.ElementType,
                    frag.OriginalText,
                    cancellationToken);

                totalLlmCalls += enrichmentResult.LlmCalls;
                totalLlmTokens += enrichmentResult.LlmTokens;

                if (string.IsNullOrWhiteSpace(enrichmentResult.EnrichedText)) continue;

                children.Add(new ChildFragment
                {
                    Bulletin = Bulletin,
                    SubSection = frag.Section,
                    ChunkText = enrichmentResult.EnrichedText
                });
            }

            var vectors = new List<float[]>(children.Count);
            const int batchSize = 100;
            for (int i = 0; i < children.Count; i += batchSize)
            {
                var batch = children.Skip(i).Take(batchSize).ToList();
                var embeddings = await _embeddingService.GenerateAsync(batch.Select(x => x.ChunkText).ToList(), cancellationToken: cancellationToken);
                for (int j = 0; j < batch.Count; j++)
                {
                    vectors.Add(embeddings[j].Vector.ToArray());
                    totalTokensEmbedded += batch[j].ChunkText.Length / 4; // Estimate
                }
            }

            // 2. Replace the previous version of the document atomically.
            var previousParentIds = new List<long>();
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                // The execution strategy may re-run this block: start each attempt from a clean state.
                _dbContext.ChangeTracker.Clear();
                parentDoc.Id = 0;
                parentDoc.Children.Clear();
                foreach (var child in children)
                {
                    child.Id = 0;
                    parentDoc.Children.Add(child);
                }

                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                previousParentIds = await _dbContext.ParentDocuments
                    .Where(p => p.DocumentId == documentId)
                    .Select(p => p.Id)
                    .ToListAsync(cancellationToken);

                if (previousParentIds.Count > 0)
                {
                    // Child fragments are removed by the ON DELETE CASCADE foreign key.
                    await _dbContext.ParentDocuments
                        .Where(p => p.DocumentId == documentId)
                        .ExecuteDeleteAsync(cancellationToken);
                }

                _dbContext.ParentDocuments.Add(parentDoc);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });

            // 3. Vector store, after the commit.
            await SyncVectorsAsync(documentId, previousParentIds, children, vectors, cancellationToken);

            sw.Stop();
            await _metricsService.TrackIngestionAsync(PipelineType.Hierarchical, Bulletin, documentId, totalTokensEmbedded, totalLlmCalls, totalLlmTokens, children.Count, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando {Bulletin} {DocumentId}", Bulletin, documentId);
            throw;
        }
    }

    private async Task SyncVectorsAsync(string documentId, IReadOnlyCollection<long> previousParentIds, IReadOnlyList<ChildFragment> children, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
    {
        var collection = VectorCollectionNames.ChildFragments;

        if (vectors.Count > 0)
        {
            await _vectorCollections.EnsureAsync(collection, vectors[0].Length, cancellationToken);
        }
        else if (!await _qdrantClient.CollectionExistsAsync(collection, cancellationToken))
        {
            return;
        }

        // Points of previous versions: matched by DocumentId (new payload) or by their old ParentId.
        var stale = new Filter();
        stale.Should.Add(MatchKeyword("DocumentId", documentId));
        foreach (var parentId in previousParentIds)
        {
            stale.Should.Add(Match("ParentId", parentId));
        }
        await _qdrantClient.DeleteAsync(collection, stale, cancellationToken: cancellationToken);

        if (vectors.Count == 0)
        {
            return;
        }

        var points = children.Select((fragment, i) => new PointStruct
        {
            Id = new PointId { Num = (ulong)fragment.Id },
            Vectors = vectors[i],
            Payload =
            {
                ["DocumentId"] = documentId,
                ["ParentId"] = fragment.ParentId,
                ["Bulletin"] = (int)fragment.Bulletin,
                ["Municipality"] = fragment.Municipality ?? "",
                ["SubSection"] = fragment.SubSection ?? "",
                ["ChunkText"] = fragment.ChunkText ?? ""
            }
        }).ToList();

        await _qdrantClient.UpsertAsync(collection, points, cancellationToken: cancellationToken);
    }
}
