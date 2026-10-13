using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using AsistenteAyuntamiento.Application.Common.VectorStore;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public class HybridRetrievalService(
    IAppDbContext dbContext,
    Kernel kernel,
    IVectorStore vectorStore,
    ILogger<HybridRetrievalService> logger) : IHybridRetrievalService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    public async Task<List<RetrievalResult>> RetrieveAsync(ExpandedQueryInfo queryInfo, int limit = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Generate Embeddings
            var embeddings = await _embeddingService.GenerateAsync(new List<string> { queryInfo.QuerySemantica }, cancellationToken: cancellationToken);
            var embeddingVector = embeddings[0].Vector.ToArray();

            // 2. Dense vector search
            IReadOnlyList<VectorSearchHit> vectorHits = [];
            try
            {
                var textFilter = string.IsNullOrWhiteSpace(queryInfo.FiltroMunicipio)
                    ? ((string Field, string Text)?)null
                    : (VectorPayloadKeys.Municipality, queryInfo.FiltroMunicipio);

                vectorHits = await vectorStore.SearchAsync(
                    VectorCollection.ChildFragments,
                    new VectorSearchRequest(embeddingVector, limit, TextFilter: textFilter),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Vector search failed, falling back to text search only.");
            }

            // 3. Query PostgreSQL (Sparse Text Search)
            var sql = """
                SELECT "Id" as "FragmentId"
                FROM ingestion."ChildFragments"
                WHERE "TsvContent" @@ websearch_to_tsquery('spanish', @tsquery)
                  AND (@municipio IS NULL OR "Municipality" ILIKE '%' || @municipio || '%')
                ORDER BY ts_rank_cd("TsvContent", websearch_to_tsquery('spanish', @tsquery)) DESC
                LIMIT @limit
                """;

            var municipioParam = string.IsNullOrWhiteSpace(queryInfo.FiltroMunicipio) ? (object)DBNull.Value : queryInfo.FiltroMunicipio;
            var parameters = new[]
            {
                new Npgsql.NpgsqlParameter("@tsquery", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object)queryInfo.QueryLexica ?? DBNull.Value },
                new Npgsql.NpgsqlParameter("@municipio", NpgsqlTypes.NpgsqlDbType.Text) { Value = municipioParam },
                new Npgsql.NpgsqlParameter("@limit", NpgsqlTypes.NpgsqlDbType.Integer) { Value = limit }
            };

            var pgResults = await dbContext.Database.SqlQueryRaw<long>(sql, parameters).ToListAsync(cancellationToken);

            // 4. In-memory Reciprocal Rank Fusion (RRF)
            var scores = new Dictionary<long, double>();

            // Add vector ranks
            for (int i = 0; i < vectorHits.Count; i++)
            {
                long id = vectorHits[i].Id;
                scores[id] = 1.0 / (60.0 + i + 1); // RRF formula: 1 / (k + rank), rank is 1-based
            }

            // Add Postgres ranks
            for (int i = 0; i < pgResults.Count; i++)
            {
                long id = pgResults[i];
                if (scores.ContainsKey(id))
                {
                    scores[id] += 1.0 / (60.0 + i + 1);
                }
                else
                {
                    scores[id] = 1.0 / (60.0 + i + 1);
                }
            }

            if (!scores.Any())
                return new List<RetrievalResult>();

            // Get top combined results
            var topIds = scores.OrderByDescending(kvp => kvp.Value)
                               .Take(limit)
                               .Select(kvp => kvp.Key)
                               .ToList();

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
                        scores[id]
                    ));
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing Hybrid RRF Retrieval for query: {Query}", queryInfo.QuerySemantica);
            throw;
        }
    }
}











