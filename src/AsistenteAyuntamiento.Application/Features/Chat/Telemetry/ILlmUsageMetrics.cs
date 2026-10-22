namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>GenAI metrics for LLM calls: duration, token usage, estimated cost and time to first token.</summary>
public interface ILlmUsageMetrics
{
    void Record(LlmCallMetrics call);

    /// <summary>Estimated cost in EUR from the <c>Ai:Pricing</c> table (0 when the model has no price).</summary>
    decimal EstimateCost(string provider, string model, long inputTokens, long outputTokens);

    /// <summary>
    /// Like <see cref="EstimateCost"/>, but null when a paid model has no price. Without a provider (older call logs)
    /// the model is looked up under every provider of the table.
    /// </summary>
    decimal? TryEstimateCost(string? provider, string model, long inputTokens, long outputTokens);
}
