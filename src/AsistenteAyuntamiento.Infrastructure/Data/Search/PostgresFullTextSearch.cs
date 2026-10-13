using AsistenteAyuntamiento.Application.Common.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace AsistenteAyuntamiento.Infrastructure.Data.Search;

/// <summary>Full-text search over <c>ingestion."ChildFragments"."TsvContent"</c> (Spanish configuration, GIN index).</summary>
public sealed class PostgresFullTextSearch(AppDbContext dbContext) : IFullTextSearch
{
    private const string ChildFragmentsSql = """
        SELECT "Id" AS "Value"
        FROM ingestion."ChildFragments"
        WHERE "TsvContent" @@ websearch_to_tsquery('spanish', @tsquery)
          AND (@municipio IS NULL OR "Municipality" ILIKE '%' || @municipio || '%')
        ORDER BY ts_rank_cd("TsvContent", websearch_to_tsquery('spanish', @tsquery)) DESC
        LIMIT @limit
        """;

    public async Task<IReadOnlyList<long>> SearchChildFragmentsAsync(string query, string? municipality, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var parameters = new object[]
        {
            new NpgsqlParameter("@tsquery", NpgsqlDbType.Text) { Value = query },
            new NpgsqlParameter("@municipio", NpgsqlDbType.Text) { Value = string.IsNullOrWhiteSpace(municipality) ? DBNull.Value : municipality },
            new NpgsqlParameter("@limit", NpgsqlDbType.Integer) { Value = limit }
        };

        return await dbContext.Database.SqlQueryRaw<long>(ChildFragmentsSql, parameters).ToListAsync(cancellationToken);
    }
}
