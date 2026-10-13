using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Generation;

public interface IClearLanguageGenerationService
{
    Task<string> GenerateResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, LlmCallScope scope, CancellationToken cancellationToken = default);
    /// <param name="scope">Tenant and pipeline for the LLM metrics of the call.</param>
    IAsyncEnumerable<StreamingChatMessageContent> GenerateStreamingResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, LlmCallScope scope, CancellationToken cancellationToken = default);
}
