namespace AsistenteAyuntamiento.Application.Common.Search;

/// <summary>Lexical (full-text) search port; PostgreSQL implementation in Infrastructure.</summary>
public interface IFullTextSearch
{
    /// <summary>
    /// Ids of the child fragments that match <paramref name="query"/> (web-search syntax), best first.
    /// <paramref name="municipality"/> restricts results to fragments whose municipality contains it.
    /// </summary>
    Task<IReadOnlyList<long>> SearchChildFragmentsAsync(string query, string? municipality, int limit, CancellationToken cancellationToken = default);
}
