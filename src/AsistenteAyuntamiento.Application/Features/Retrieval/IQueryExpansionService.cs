namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public interface IQueryExpansionService
{
    Task<ExpandedQueryInfo> ExpandQueryAsync(string userQuery, CancellationToken cancellationToken = default);
}



