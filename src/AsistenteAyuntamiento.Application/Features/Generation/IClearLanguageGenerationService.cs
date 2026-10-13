using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Generation;

public interface IClearLanguageGenerationService
{
    Task<string> GenerateResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, CancellationToken cancellationToken = default);
    IAsyncEnumerable<StreamingChatMessageContent> GenerateStreamingResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, CancellationToken cancellationToken = default);
}
