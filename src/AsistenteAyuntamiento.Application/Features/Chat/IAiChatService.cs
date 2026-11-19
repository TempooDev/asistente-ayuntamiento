using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

public interface IAiChatService
{
    IAsyncEnumerable<string> GetStreamingCompletionAsync(ChatHistory history, string tenantId, string userId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AsistenteAyuntamiento.Application.Features.Chat.DTOs.ArenaStreamChunk> GetArenaStreamingCompletionAsync(ChatHistory history, string tenantId, string userId, CancellationToken cancellationToken = default);
}
