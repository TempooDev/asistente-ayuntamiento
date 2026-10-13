using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public interface IQueryExpansionService
{
    /// <param name="scope">Tenant and pipeline for the LLM metrics (the operation is set to query expansion).</param>
    Task<ExpandedQueryInfo> ExpandQueryAsync(string userQuery, LlmCallScope scope, CancellationToken cancellationToken = default);
}



