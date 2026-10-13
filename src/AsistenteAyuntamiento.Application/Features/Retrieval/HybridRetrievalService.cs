using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Common.Search;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public class HybridRetrievalService(
    IAppDbContext dbContext,
    Kernel kernel,
    IVectorStore vectorStore,
    IFullTextSearch fullTextSearch,
    ILogger<HybridRetrievalService> logger) : IHybridRetrievalService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    public async Task<List<RetrievalResult>> RetrieveAsync(ExpandedQueryInfo queryInfo, int limit = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Generate Embeddings
            var embeddings = await _embeddingService.GenerateAsync(new List<string> { queryInfo.SemanticQuery }, cancellationToken: cancellationToken);
            var embeddingVector = embeddings[0].Vector.ToArray();

            // 2. Dense vector search
            IReadOnlyList<VectorSearchHit> vectorHits = [];
            try
            {
                var textFilter = string.IsNullOrWhiteSpace(queryInfo.MunicipalityFilter)
                    ? ((string Field, string Text)?)null
                    : (VectorPayloadKeys.Municipality, queryInfo.MunicipalityFilter);

                vectorHits = await vectorStore.SearchAsync(
                    VectorCollection.ChildFragments,
                    new VectorSearchRequest(embeddingVector, limit, TextFilter: textFilter),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Vector search failed, falling back to text search only.");
            }

            // 3. Sparse (full-text) search
            var pgResults = await fullTextSearch.SearchChildFragmentsAsync(queryInfo.LexicalQuery ?? string.Empty, queryInfo.MunicipalityFilter, limit, cancellationToken);

            // 4. Reciprocal Rank Fusion
            var fused = Fuse(vectorHits.Select(h => h.Id).ToList(), pgResults, limit);
            if (fused.Count == 0)
                return new List<RetrievalResult>();

            var scores = fused.ToDictionary(f => f.Id, f => f.Score);
            var topIds = fused.Select(f => f.Id).ToList();

            // 5. Fetch full entities
            var fragmentsWithParents = await dbContext.ChildFragments
                .AsNoTracking()
                .Include(c => c.Parent)
                .Where(c => topIds.Contains(c.Id))
                .ToListAsync(cancellationToken);

            var results = new List<RetrievalResult>();
            foreach (var id in topIds) // Keep sorted order
            {
                var frag = fragmentsWithParents.FirstOrDefault(f => f.Id == id);
                if (frag != null && frag.Parent != null)
                {
                    results.Add(new RetrievalResult(
                        frag.Id,
                        frag.ChunkText ?? "",
                        frag.Parent.Id,
                        frag.Parent.FullText ?? "",
                        scores[id],
                        frag.Parent.DocumentId,
                        frag.Parent.NormTitle,
                        frag.Parent.Bulletin,
                        frag.Parent.IssuingBody,
                        frag.Parent.PublicationDate
                    ));
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing Hybrid RRF Retrieval for query: {Query}", queryInfo.SemanticQuery);
            throw;
        }
    }

    /// <summary>RRF constant: dampens the weight of the top ranks (Cormack et al. use 60).</summary>
    internal const int RrfK = 60;

    /// <summary>
    /// Reciprocal Rank Fusion: each list adds <c>1 / (k + rank)</c> (1-based rank) to an id's score; the best
    /// <paramref name="limit"/> ids are returned, highest score first (ties keep dense-search order).
    /// </summary>
    internal static IReadOnlyList<(long Id, double Score)> Fuse(IReadOnlyList<long> denseRanking, IReadOnlyList<long> sparseRanking, int limit)
    {
        var scores = new Dictionary<long, double>();
        foreach (var ranking in new[] { denseRanking, sparseRanking })
        {
            for (var i = 0; i < ranking.Count; i++)
            {
                scores[ranking[i]] = scores.GetValueOrDefault(ranking[i]) + 1.0 / (RrfK + i + 1);
            }
        }

        return scores
            .OrderByDescending(kvp => kvp.Value)
            .Take(limit)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }
}
