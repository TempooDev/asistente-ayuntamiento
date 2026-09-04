using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

public interface IAiChatService
{
    Task<AiCompletionResult> GetCompletionAsync(ChatHistory history, string tenantId, string userId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> GetStreamingCompletionAsync(ChatHistory history, string tenantId, string userId, CancellationToken cancellationToken = default);
}
