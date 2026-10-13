using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Snapshot returned by the /api/ai/metrics endpoint.
/// </summary>
public sealed class AiMetricsSnapshot
{
    public DateTime GeneratedAtUtc { get; init; }
    public long TotalCalls { get; init; }
    public long SucceededCalls { get; init; }
    public long FailedCalls { get; init; }
    public double SuccessRate { get; init; }
    public double AverageDurationMs { get; init; }

    // Token consumption aggregates
    public long TotalInputTokens { get; init; }
    public long TotalOutputTokens { get; init; }
    public long TotalTokens { get; init; }
    public double AverageTokensPerCall { get; init; }

    public List<AiCallRecord> RecentCalls { get; init; } = [];
}
