using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Invokes the tenant's configured chat model, switches to the platform fallback model when the tenant's quota is
/// exceeded, and records usage. The history must already contain any RAG context.
/// </summary>
public interface IChatStreamingService
{
    /// <summary>Never throws for model errors: they come back as a failed result with a typed error code.</summary>
    Task<AiCompletionResult> CompleteAsync(ChatHistory history, string tenantId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Streams text chunks. Model errors are streamed as a user-facing message, never thrown.</summary>
    IAsyncEnumerable<string> StreamAsync(ChatHistory history, string tenantId, string userId, CancellationToken cancellationToken = default);
}
