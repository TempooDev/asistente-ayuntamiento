using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public interface IQueryExpansionService
{
    /// <param name="scope">Tenant and pipeline for the LLM metrics (the operation is set to query expansion).</param>
    Task<ExpandedQueryInfo> ExpandQueryAsync(string userQuery, LlmCallScope scope, CancellationToken cancellationToken = default) =>
        ExpandQueryAsync(userQuery, [], scope, cancellationToken);

    /// <summary>
    /// Expands a question that may refer to earlier turns ("el convenio", "estas ayudas"): the queries name what it
    /// refers to, so the search finds that document.
    /// </summary>
    /// <param name="previousMessages">The conversation before the question (see <c>ConversationContext.Previous</c>).</param>
    Task<ExpandedQueryInfo> ExpandQueryAsync(string userQuery, IReadOnlyList<ChatMessageContent> previousMessages, LlmCallScope scope, CancellationToken cancellationToken = default);
}
