namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public interface IHybridRetrievalService
{
    Task<List<RetrievalResult>> RetrieveAsync(ExpandedQueryInfo queryInfo, int limit = 5, CancellationToken cancellationToken = default);
}








