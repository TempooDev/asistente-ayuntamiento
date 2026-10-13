using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Invokes the tenant's configured chat model, switches to the platform fallback model when the tenant's quota is
/// exceeded, and records usage. The history must already contain any RAG context.
/// </summary>
public interface IChatStreamingService
{
    /// <summary>Never throws for model errors: they come back as a failed result with a typed error code.</summary>
    /// <param name="pipeline">Value of <c>app.pipeline</c> for the metrics (see <see cref="Telemetry.LlmPipelines"/>).</param>
    Task<AiCompletionResult> CompleteAsync(ChatHistory history, string tenantId, string userId, string pipeline, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams text chunks. A model error before any text is streamed as a user-facing message; after some text it is
    /// recorded as a failure and thrown as <see cref="LlmStreamInterruptedException"/>. A stream the caller cancelled
    /// just ends.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(ChatHistory history, string tenantId, string userId, string pipeline, CancellationToken cancellationToken = default);
}
