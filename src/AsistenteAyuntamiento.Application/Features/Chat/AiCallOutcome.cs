namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>What happened in one model invocation, for <see cref="IAiCallRecorder"/>.</summary>
public sealed record AiCallOutcome(
    string ModelId,
    string TenantId,
    string UserId,
    bool Success,
    double DurationMs,
    TokenUsageInfo Usage,
    int PromptLength,
    int ResponseLength,
    int HistoryMessageCount,
    string? ErrorCode = null,
    string? ErrorDetail = null);
