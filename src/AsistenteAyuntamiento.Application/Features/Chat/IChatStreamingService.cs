using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Invokes the tenant's configured chat model, switches to the platform fallback model when the tenant's quota is
/// exceeded, and records usage. The history must already contain any RAG context.
/// </summary>
public interface IChatStreamingService
{
    /// <summary>
    /// Streams text chunks. A model error, before or after some text, is recorded as a failure and thrown as
    /// <see cref="LlmStreamInterruptedException"/> (the chat turns one before any text into a message, see
    /// <see cref="AiChatService"/>). A stream the caller cancelled just ends.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(ChatHistory history, string tenantId, string userId, string pipeline, CancellationToken cancellationToken = default);
}
