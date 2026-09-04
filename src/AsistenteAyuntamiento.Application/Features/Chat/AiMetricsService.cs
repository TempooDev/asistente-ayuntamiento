using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Tracks AI model invocation metrics using OpenTelemetry instruments
/// and an in-memory store for the REST endpoint.
/// </summary>
public sealed class AiMetricsService : IAiMetricsService
{
    // ── OpenTelemetry instruments ────────────────────────────────────────
    public static readonly string MeterName = "AsistenteAyuntamiento.Ai";

    private static readonly Meter _meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> _callsTotal =
        _meter.CreateCounter<long>("ai.calls.total", "calls", "Total number of calls to the AI model");

    private static readonly Counter<long> _callsSucceeded =
        _meter.CreateCounter<long>("ai.calls.succeeded", "calls", "Number of successful AI model calls");

    private static readonly Counter<long> _callsFailed =
        _meter.CreateCounter<long>("ai.calls.failed", "calls", "Number of failed AI model calls");

    private static readonly Histogram<double> _callDuration =
        _meter.CreateHistogram<double>("ai.call.duration", "ms", "Duration of AI model calls in milliseconds");

    private static readonly Histogram<int> _responseLength =
        _meter.CreateHistogram<int>("ai.response.length", "chars", "Length of AI model responses in characters");

    private static readonly Histogram<int> _inputTokens =
        _meter.CreateHistogram<int>("ai.tokens.input", "tokens", "Number of input/prompt tokens per request");

    private static readonly Histogram<int> _outputTokens =
        _meter.CreateHistogram<int>("ai.tokens.output", "tokens", "Number of output/completion tokens per request");

    private static readonly Counter<long> _totalTokensCounter =
        _meter.CreateCounter<long>("ai.tokens.total", "tokens", "Cumulative token consumption");

    // ── ActivitySource for distributed tracing ──────────────────────────
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    // ── In-memory log (bounded ring-buffer of recent calls) ─────────────
    private const int MaxRecentCalls = 200;
    private readonly ConcurrentQueue<AiCallRecord> _recentCalls = new();
    private int _recentCallsCount;

    // Aggregate counters (thread-safe via Interlocked)
    private long _totalCalls;
    private long _successCalls;
    private long _failedCalls;
    private double _totalDurationMs;
    private long _totalInputTokens;
    private long _totalOutputTokens;

    /// <summary>
    /// Records a completed AI call, updating both OTel instruments and the in-memory store.
    /// </summary>
    public void RecordCall(AiCallRecord record)
    {
        // OpenTelemetry counters / histograms
        var tags = new TagList
        {
            { "ai.model", record.ModelId },
            { "ai.tenant", record.TenantId },
            { "ai.status", record.Success ? "success" : "error" }
        };

        _callsTotal.Add(1, tags);
        _callDuration.Record(record.DurationMs, tags);

        if (record.Success)
        {
            _callsSucceeded.Add(1, tags);
            _responseLength.Record(record.ResponseLength, tags);

            if (record.InputTokens > 0)
                _inputTokens.Record(record.InputTokens, tags);
            if (record.OutputTokens > 0)
                _outputTokens.Record(record.OutputTokens, tags);
            if (record.TotalTokens > 0)
                _totalTokensCounter.Add(record.TotalTokens, tags);
        }
        else
        {
            _callsFailed.Add(1, tags);
        }

        // In-memory aggregates
        Interlocked.Increment(ref _totalCalls);
        if (record.Success)
            Interlocked.Increment(ref _successCalls);
        else
            Interlocked.Increment(ref _failedCalls);

        // Thread-safe double add via spin loop
        double initialValue, newValue;
        do
        {
            initialValue = _totalDurationMs;
            newValue = initialValue + record.DurationMs;
        } while (Interlocked.CompareExchange(ref _totalDurationMs, newValue, initialValue) != initialValue);

        // Token aggregates
        if (record.InputTokens > 0)
            Interlocked.Add(ref _totalInputTokens, record.InputTokens);
        if (record.OutputTokens > 0)
            Interlocked.Add(ref _totalOutputTokens, record.OutputTokens);

        // Bounded queue
        _recentCalls.Enqueue(record);
        var currentCount = Interlocked.Increment(ref _recentCallsCount);
        while (currentCount > MaxRecentCalls)
        {
            if (_recentCalls.TryDequeue(out _))
            {
                currentCount = Interlocked.Decrement(ref _recentCallsCount);
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of the current AI metrics.
    /// </summary>
    public AiMetricsSnapshot GetSnapshot()
    {
        var total = Interlocked.Read(ref _totalCalls);
        var succeeded = Interlocked.Read(ref _successCalls);
        var failed = Interlocked.Read(ref _failedCalls);
        var totalDuration = _totalDurationMs;
        var inputTokens = Interlocked.Read(ref _totalInputTokens);
        var outputTokens = Interlocked.Read(ref _totalOutputTokens);
        var recentCalls = _recentCalls.ToArray();

        return new AiMetricsSnapshot
        {
            GeneratedAtUtc = DateTime.UtcNow,
            TotalCalls = total,
            SucceededCalls = succeeded,
            FailedCalls = failed,
            SuccessRate = total > 0 ? Math.Round((double)succeeded / total * 100, 2) : 0,
            AverageDurationMs = total > 0 ? Math.Round(totalDuration / total, 2) : 0,
            TotalInputTokens = inputTokens,
            TotalOutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            AverageTokensPerCall = succeeded > 0 ? Math.Round((double)(inputTokens + outputTokens) / succeeded, 1) : 0,
            RecentCalls = recentCalls.OrderByDescending(c => c.Timestamp).Take(50).ToList()
        };
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────

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
