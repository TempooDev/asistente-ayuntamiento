using AsistenteAyuntamiento.Domain.Common.Enums;
using System.Diagnostics;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.AI;
using Qdrant.Client;

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

    protected BaseHierarchicalIngestionProcessor(
        IAppDbContext dbContext,
        IFragmentEnrichmentService enrichmentService,
        IIngestionMetricsService metricsService,
        QdrantClient qdrantClient,
        ILogger logger,
        Kernel kernel)
    {
        _dbContext = dbContext;
        _enrichmentService = enrichmentService;
        _metricsService = metricsService;
        _qdrantClient = qdrantClient;
        _logger = logger;
        _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    }

    protected abstract BulletinType Bulletin { get; }
    
    protected abstract Task<ParentDocument?> ExtractParentDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken);
    
    protected abstract Task<IEnumerable<RawFragment>> ExtractFragmentsAsync(string blobPath, string documentId, ParentDocument parentDoc, CancellationToken cancellationToken);

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
        int chunksGenerated = 0;

        try
        {
            var parentDoc = await ExtractParentDocumentAsync(blobPath, documentId, cancellationToken);
            if (parentDoc == null) return;

            _dbContext.ParentDocuments.Add(parentDoc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var fragments = await ExtractFragmentsAsync(blobPath, documentId, parentDoc, cancellationToken);

            var qdrantPoints = new List<(ChildFragment Fragment, float[] Vector)>();

            int fragmentCount = fragments.Count();
            int current = 0;

            foreach (var frag in fragments)
            {
                current++;
                if (current % 10 == 0 || current == fragmentCount)
                {
                    _logger.LogInformation("[{Bulletin} {DocumentId}] Procesando fragmento {Current}/{Total} de la sección '{Section}'...", Bulletin, documentId, current, fragmentCount, frag.Section);
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

                var embeddings = await _embeddingService.GenerateAsync(new List<string> { enrichmentResult.EnrichedText }, cancellationToken: cancellationToken);
                var rawVector = embeddings[0].Vector.ToArray();
                totalTokensEmbedded += enrichmentResult.EnrichedText.Length / 4; // Estimate

                var childFragment = new ChildFragment
                {
                    ParentId = parentDoc.Id,
                    Bulletin = Bulletin,
                    SubSection = frag.Section,
                    ChunkText = enrichmentResult.EnrichedText
                };

                _dbContext.ChildFragments.Add(childFragment);
                qdrantPoints.Add((childFragment, rawVector));
                chunksGenerated++;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (qdrantPoints.Any())
            {
                var points = qdrantPoints.Select(p => new Qdrant.Client.Grpc.PointStruct
                {
                    Id = new Qdrant.Client.Grpc.PointId { Num = (ulong)p.Fragment.Id },
                    Vectors = p.Vector,
                    Payload =
                    {
                        ["ParentId"] = p.Fragment.ParentId,
                        ["Bulletin"] = (int)p.Fragment.Bulletin,
                        ["Municipality"] = p.Fragment.Municipality ?? "",
                        ["SubSection"] = p.Fragment.SubSection ?? "",
                        ["ChunkText"] = p.Fragment.ChunkText ?? ""
                    }
                }).ToList();

                await _qdrantClient.UpsertAsync("child_fragments", points, cancellationToken: cancellationToken);
            }

            sw.Stop();
            await _metricsService.TrackIngestionAsync(PipelineType.Hierarchical, Bulletin, documentId, totalTokensEmbedded, totalLlmCalls, totalLlmTokens, chunksGenerated, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando {Bulletin} {DocumentId}", Bulletin, documentId);
            throw;
        }
    }
}


