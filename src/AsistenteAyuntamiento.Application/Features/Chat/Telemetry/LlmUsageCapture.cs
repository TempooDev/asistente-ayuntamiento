namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>
/// Collects the LLM calls recorded by <see cref="LlmUsageMetrics"/> in the current async flow, so the evaluation can
/// report the tokens of one answer (expansion included) without changing the services that make the calls.
/// </summary>
public static class LlmUsageCapture
{
    private static readonly AsyncLocal<List<LlmCallMetrics>?> Current = new();

    /// <summary>Starts capturing for the calling flow until the scope is disposed.</summary>
    public static Scope Begin()
    {
        var scope = new Scope(Current.Value);
        Current.Value = scope.Calls;
        return scope;
    }

    internal static void Add(LlmCallMetrics call)
    {
        if (Current.Value is { } calls)
        {
            lock (calls)
            {
                calls.Add(call);
            }
        }
    }

    public sealed class Scope(List<LlmCallMetrics>? previous) : IDisposable
    {
        internal List<LlmCallMetrics> Calls { get; } = [];

        public TokenUsageInfo Total
        {
            get
            {
                lock (Calls)
                {
                    return TokenUsageInfo.Of((int)Calls.Sum(c => c.InputTokens), (int)Calls.Sum(c => c.OutputTokens));
                }
            }
        }

        public void Dispose() => Current.Value = previous;
    }
}
