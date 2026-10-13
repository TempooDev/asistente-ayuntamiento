using System.Diagnostics.Metrics;

namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>
/// Metric and attribute names for LLM calls, in one place because the OpenTelemetry GenAI conventions are still in
/// development. Checked against open-telemetry/semantic-conventions-genai (docs/gen-ai, October 2026):
/// <c>gen_ai.client.operation.duration</c> (s) and the <c>gen_ai.client.inference.usage.*_tokens</c> counters, with
/// <c>gen_ai.provider.name</c>. There is no client time-to-first-token metric yet, so ours lives under <c>app.</c>.
/// </summary>
public static class GenAiTelemetry
{
    public const string MeterName = "AsistenteAyuntamiento.Ai";

    internal static readonly Meter Meter = new(MeterName, "2.0.0");

    public static class Metrics
    {
        public const string OperationDuration = "gen_ai.client.operation.duration";
        public const string InputTokens = "gen_ai.client.inference.usage.input_tokens";
        public const string OutputTokens = "gen_ai.client.inference.usage.output_tokens";
        public const string TimeToFirstToken = "app.llm.time_to_first_token";
        public const string Cost = "app.llm.cost";
    }

    public static class Attributes
    {
        public const string OperationName = "gen_ai.operation.name";
        public const string ProviderName = "gen_ai.provider.name";
        public const string RequestModel = "gen_ai.request.model";
        public const string TokenModality = "gen_ai.token.modality";
        public const string ErrorType = "error.type";
        public const string TenantId = "app.tenant_id";
        public const string Pipeline = "app.pipeline";
        public const string Fallback = "app.fallback";
    }

    public const string ChatOperation = "chat";
    public const string TextModality = "text";

    /// <summary>
    /// <c>gen_ai.provider.name</c> for our configured providers: the well-known value where one exists
    /// (<c>openai</c>, <c>gcp.gemini</c>), the lowercase provider name otherwise.
    /// </summary>
    public static string ProviderName(string? configuredProvider) => configuredProvider?.ToLowerInvariant() switch
    {
        null or "" => "unknown",
        "google" or "gemini" => "gcp.gemini",
        var other => other,
    };
}
