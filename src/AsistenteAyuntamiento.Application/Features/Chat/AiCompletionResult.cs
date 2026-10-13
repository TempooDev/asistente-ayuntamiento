namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>Result of a non-streaming chat completion. Failures carry a typed <see cref="ErrorCode"/>.</summary>
/// <param name="Success">Whether the completion succeeded.</param>
/// <param name="Content">The generated text, or the user-facing error message on failure.</param>
/// <param name="DurationMs">Elapsed time in milliseconds.</param>
/// <param name="ErrorMessage">User-facing error message when <paramref name="Success"/> is <c>false</c>; otherwise <c>null</c>.</param>
/// <param name="TokenUsage">Token consumption for this request.</param>
/// <param name="Sources">Documents used as context, if any.</param>
/// <param name="ErrorCode">One of <see cref="LlmErrorCodes"/> on failure.</param>
public sealed record AiCompletionResult(
    bool Success,
    string Content,
    double DurationMs,
    string? ErrorMessage,
    TokenUsageInfo TokenUsage,
    List<DocumentSource>? Sources = null,
    string? ErrorCode = null);
