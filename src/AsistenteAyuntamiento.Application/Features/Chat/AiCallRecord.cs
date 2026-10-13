using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Represents a single AI model invocation record.
/// </summary>
public sealed class AiCallRecord
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ModelId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public double DurationMs { get; init; }
    public int PromptLength { get; init; }
    public int ResponseLength { get; init; }
    public string? ErrorMessage { get; init; }
    public int HistoryMessageCount { get; init; }

    // Token consumption
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens { get; init; }
}
