using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public record RetrievalResult(long FragmentId, string ChunkText, long ParentId, string ParentFullText, double RrfScore);

public interface IHybridRetrievalService
{
    Task<List<RetrievalResult>> RetrieveAsync(ExpandedQueryInfo queryInfo, int limit = 5, CancellationToken cancellationToken = default);
}

public class HybridRetrievalService(IAppDbContext dbContext, Kernel kernel, ILogger<HybridRetrievalService> logger) : IHybridRetrievalService
{

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    public async Task<List<RetrievalResult>> RetrieveAsync(ExpandedQueryInfo queryInfo, int limit = 5, CancellationToken cancellationToken = default)
    {
        var embeddings = await _embeddingService.GenerateAsync(new List<string> { queryInfo.QuerySemantica }, cancellationToken: cancellationToken);
        var embeddingVector = embeddings[0].Vector.ToArray();

        

        // C# 11 Raw String Literals make this highly readable and maintainable
        var sql = """
            WITH vector_search AS (
                SELECT "Id",
                       RANK() OVER (ORDER BY "Embedding" <=> @embedding) as rank_vector
                FROM ingestion."ChildFragments"
                WHERE (@municipio IS NULL OR "Municipality" ILIKE '%' || @municipio || '%')
                ORDER BY "Embedding" <=> @embedding
                LIMIT @limit
            ),
            keyword_search AS (
                SELECT "Id",
                       RANK() OVER (ORDER BY ts_rank_cd("TsvContent", websearch_to_tsquery('spanish', @tsquery)) DESC) as rank_ts
                FROM ingestion."ChildFragments"
                WHERE "TsvContent" @@ websearch_to_tsquery('spanish', @tsquery)
                  AND (@municipio IS NULL OR "Municipality" ILIKE '%' || @municipio || '%')
                ORDER BY ts_rank_cd("TsvContent", websearch_to_tsquery('spanish', @tsquery)) DESC
                LIMIT @limit
            )
            SELECT 
                COALESCE(v."Id", k."Id") as "FragmentId",
                COALESCE(1.0 / (60.0 + v.rank_vector), 0.0) + COALESCE(1.0 / (60.0 + k.rank_ts), 0.0) as "RrfScore"
            FROM vector_search v
            FULL OUTER JOIN keyword_search k ON v."Id" = k."Id"
            ORDER BY "RrfScore" DESC
            LIMIT @limit;
            """;

        var npgsqlVector = new Pgvector.Vector(embeddingVector);
        var municipioParam = string.IsNullOrWhiteSpace(queryInfo.FiltroMunicipio) ? (object)DBNull.Value : queryInfo.FiltroMunicipio;

        // Use standard NpgsqlParameters
        var parameters = new[]
        {
            new Npgsql.NpgsqlParameter("@embedding", npgsqlVector),
            new Npgsql.NpgsqlParameter("@tsquery", queryInfo.QueryLexica),
            new Npgsql.NpgsqlParameter("@municipio", municipioParam),
            new Npgsql.NpgsqlParameter("@limit", limit)
        };

        var fragmentScores = await dbContext.Database.SqlQueryRaw<RrfRow>(sql, parameters)
            .ToListAsync(cancellationToken);

        if (!fragmentScores.Any())
            return new List<RetrievalResult>();

        var fragmentIds = fragmentScores.Select(f => f.FragmentId).ToList();

        // Resolve fragments and parent documents via LINQ
        var fragmentsWithParents = await db.Set<ChildFragment>()
            .Include(c => c.Parent)
            .Where(c => fragmentIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        var results = new List<RetrievalResult>();
        foreach (var score in fragmentScores)
        {
            var frag = fragmentsWithParents.FirstOrDefault(f => f.Id == score.FragmentId);
            if (frag != null && frag.Parent != null)
            {
                results.Add(new RetrievalResult(
                    frag.Id,
                    frag.ChunkText ?? "",
                    frag.Parent.Id,
                    frag.Parent.FullText ?? "",
                    score.RrfScore
                ));
            }
        }

        return results.OrderByDescending(r => r.RrfScore).ToList();
    }

    // Internal struct to map the RAW SQL result
    private class RrfRow
    {
        public long FragmentId { get; set; }
        public double RrfScore { get; set; }
    }
}





