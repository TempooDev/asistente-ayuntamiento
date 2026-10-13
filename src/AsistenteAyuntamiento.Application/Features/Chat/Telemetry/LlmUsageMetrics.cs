using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>
/// Records LLM calls with the GenAI conventions plus <c>app.tenant_id</c>, <c>app.pipeline</c> and <c>app.fallback</c>.
/// Labels are low-cardinality on purpose (tens of tenants and models); user and session ids never become labels.
/// </summary>
/// <remarks>
/// Prices come from <c>Ai:Pricing:{provider}:{model}:{Input,Output}</c>, in EUR per million tokens. Ollama runs on
/// our own hardware and costs 0 without a warning; any other model without a price records 0 and is logged once.
/// </remarks>
public sealed class LlmUsageMetrics(IConfiguration configuration, ILogger<LlmUsageMetrics> logger) : ILlmUsageMetrics
{
    private const string SelfHostedProvider = "ollama";

    private static readonly Histogram<double> Duration = GenAiTelemetry.Meter.CreateHistogram<double>(
        GenAiTelemetry.Metrics.OperationDuration, "s", "GenAI operation duration.");

    private static readonly Counter<long> InputTokens = GenAiTelemetry.Meter.CreateCounter<long>(
        GenAiTelemetry.Metrics.InputTokens, "{token}", "Input (prompt) tokens used.");

    private static readonly Counter<long> OutputTokens = GenAiTelemetry.Meter.CreateCounter<long>(
        GenAiTelemetry.Metrics.OutputTokens, "{token}", "Output (completion) tokens used.");

    private static readonly Histogram<double> TimeToFirstToken = GenAiTelemetry.Meter.CreateHistogram<double>(
        GenAiTelemetry.Metrics.TimeToFirstToken, "s", "Time from the request to the first streamed chunk, measured by the API.");

    // "{EUR}" is an annotation: exporters keep the name as is (app_llm_cost_total in Prometheus).
    private static readonly Counter<double> Cost = GenAiTelemetry.Meter.CreateCounter<double>(
        GenAiTelemetry.Metrics.Cost, "{EUR}", "Estimated LLM cost in EUR, from the configured price table.");

    private readonly ConcurrentDictionary<string, byte> _unpricedModels = new();

    public void Record(LlmCallMetrics call)
    {
        var provider = GenAiTelemetry.ProviderName(call.Provider);
        var tags = new TagList
        {
            { GenAiTelemetry.Attributes.OperationName, GenAiTelemetry.ChatOperation },
            { GenAiTelemetry.Attributes.ProviderName, provider },
            { GenAiTelemetry.Attributes.RequestModel, call.Model },
            { GenAiTelemetry.Attributes.TenantId, call.TenantId },
            { GenAiTelemetry.Attributes.Pipeline, call.Pipeline },
            { GenAiTelemetry.Attributes.Fallback, call.Fallback },
        };

        var durationTags = tags;
        if (call.ErrorType is not null)
        {
            durationTags.Add(GenAiTelemetry.Attributes.ErrorType, call.ErrorType);
        }
        Duration.Record(call.Duration.TotalSeconds, durationTags);

        if (call.TimeToFirstToken is { } ttft)
        {
            TimeToFirstToken.Record(ttft.TotalSeconds, tags);
        }

        if (call.InputTokens <= 0 && call.OutputTokens <= 0)
        {
            return;
        }

        var tokenTags = tags;
        tokenTags.Add(GenAiTelemetry.Attributes.TokenModality, GenAiTelemetry.TextModality);
        if (call.InputTokens > 0) InputTokens.Add(call.InputTokens, tokenTags);
        if (call.OutputTokens > 0) OutputTokens.Add(call.OutputTokens, tokenTags);

        Cost.Add((double)EstimateCost(call.Provider, call.Model, call.InputTokens, call.OutputTokens), tags);
    }

    public decimal EstimateCost(string provider, string model, long inputTokens, long outputTokens)
    {
        if (string.Equals(provider, SelfHostedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        var price = configuration.GetSection($"Ai:Pricing:{provider.ToLowerInvariant()}:{model}");
        var input = price.GetValue<decimal?>("Input");
        var output = price.GetValue<decimal?>("Output");
        if (input is null && output is null)
        {
            if (_unpricedModels.TryAdd($"{provider}/{model}", 0))
            {
                logger.LogWarning("No price configured for model {Model} of provider {Provider} (Ai:Pricing); its cost is recorded as 0", model, provider);
            }
            return 0m;
        }

        return (inputTokens * (input ?? 0m) + outputTokens * (output ?? 0m)) / 1_000_000m;
    }
}
