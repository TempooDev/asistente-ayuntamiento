using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Generation;

public interface IClearLanguageGenerationService
{
    Task<string> GenerateResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, LlmCallScope scope, CancellationToken cancellationToken = default);
    /// <param name="scope">Tenant and pipeline for the LLM metrics of the call.</param>
    /// <param name="previousMessages">The conversation before the question, so a follow-up is answered about the same topic.</param>
    IAsyncEnumerable<StreamingChatMessageContent> GenerateStreamingResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, LlmCallScope scope,
        IReadOnlyList<ChatMessageContent>? previousMessages = null, CancellationToken cancellationToken = default);

    /// <summary>The legal text the answer prompt would carry for <paramref name="retrievedContext"/> (for the evaluation).</summary>
    string BuildPromptContext(List<RetrievalResult> retrievedContext);
}
