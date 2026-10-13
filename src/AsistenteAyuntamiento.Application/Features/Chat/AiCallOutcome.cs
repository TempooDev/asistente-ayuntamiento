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
    string? ErrorDetail = null)
{
    /// <summary>Configured provider of the model that answered (<c>ollama</c>, <c>openai</c>, <c>openrouter</c>, <c>google</c>).</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>Value of <c>app.pipeline</c> (see <see cref="Telemetry.LlmPipelines"/>).</summary>
    public string Pipeline { get; init; } = Telemetry.LlmPipelines.Baseline;

    /// <summary>The platform fallback model answered because the tenant's quota was exhausted.</summary>
    public bool Fallback { get; init; }

    /// <summary>Streaming only: time until the first chunk arrived.</summary>
    public TimeSpan? TimeToFirstToken { get; init; }
}
