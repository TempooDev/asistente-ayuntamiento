namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>One finished LLM call, as recorded in metrics. Never carries prompt or completion text.</summary>
public sealed record LlmCallMetrics(
    string Provider,
    string Model,
    string TenantId,
    string Pipeline,
    bool Fallback,
    TimeSpan Duration,
    long InputTokens,
    long OutputTokens,
    TimeSpan? TimeToFirstToken = null,
    string? ErrorType = null);
