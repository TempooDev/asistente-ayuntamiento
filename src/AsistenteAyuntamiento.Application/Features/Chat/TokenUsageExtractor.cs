using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Reads token usage from Semantic Kernel response metadata. Providers expose it differently: a <c>Usage</c>
/// object (OpenAI and OpenAI-compatible: <c>ChatTokenUsage</c> with <c>InputTokenCount</c>/<c>OutputTokenCount</c>),
/// flat keys (Gemini: <c>PromptTokenCount</c>/<c>CandidatesTokenCount</c>; Ollama) or the inner OllamaSharp response.
/// In streaming, OpenAI sends the usage in a last chunk without content.
/// </summary>
public static class TokenUsageExtractor
{
    public static TokenUsageInfo Extract(IReadOnlyDictionary<string, object?>? metadata, object? innerContent = null)
    {
        if (metadata is null)
        {
            return TokenUsageInfo.Empty;
        }

        // Strategy 1: a "Usage" object with standard properties
        if (metadata.TryGetValue("Usage", out var usage) && usage is not null)
        {
            var input = GetIntProperty(usage, "InputTokenCount") ?? GetIntProperty(usage, "PromptTokens") ?? GetIntProperty(usage, "InputTokens") ?? 0;
            var output = GetIntProperty(usage, "OutputTokenCount") ?? GetIntProperty(usage, "CompletionTokens") ?? GetIntProperty(usage, "OutputTokens") ?? 0;
            if (input > 0 || output > 0)
            {
                return TokenUsageInfo.Of(input, output);
            }
        }

        // Strategy 2: Ollama-specific flat metadata keys
        var flatInput = GetMetadataInt(metadata, "prompt_eval_count") ?? GetMetadataInt(metadata, "PromptEvalCount") ?? GetMetadataInt(metadata, "PromptTokenCount") ?? 0;
        var flatOutput = GetMetadataInt(metadata, "eval_count") ?? GetMetadataInt(metadata, "EvalCount") ?? GetMetadataInt(metadata, "CandidatesTokenCount") ?? GetMetadataInt(metadata, "CompletionTokenCount") ?? 0;
        if (flatInput > 0 || flatOutput > 0)
        {
            return TokenUsageInfo.Of(flatInput, flatOutput);
        }

        // Strategy 3: the provider's own response object (OllamaSharp)
        if (innerContent is not null)
        {
            var input = GetIntProperty(innerContent, "PromptEvalCount") ?? GetIntProperty(innerContent, "prompt_eval_count") ?? 0;
            var output = GetIntProperty(innerContent, "EvalCount") ?? GetIntProperty(innerContent, "eval_count") ?? 0;
            if (input > 0 || output > 0)
            {
                return TokenUsageInfo.Of(input, output);
            }
        }

        return TokenUsageInfo.Empty;
    }

    public static TokenUsageInfo Extract(ChatMessageContent response) => Extract(response.Metadata, response.InnerContent);

    private static int? GetMetadataInt(IReadOnlyDictionary<string, object?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) ? ToInt(value) : null;

    private static int? GetIntProperty(object obj, string propertyName) =>
        obj.GetType().GetProperty(propertyName) is { } property ? ToInt(property.GetValue(obj)) : null;

    private static int? ToInt(object? value) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => null
    };
}
