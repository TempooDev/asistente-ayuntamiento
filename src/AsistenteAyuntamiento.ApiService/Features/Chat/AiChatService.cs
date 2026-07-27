using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

/// <summary>
/// Result of an AI chat completion request.
/// </summary>
/// <param name="Success">Whether the completion succeeded.</param>
/// <param name="Content">The generated text content, or an error message on failure.</param>
/// <param name="DurationMs">Elapsed time in milliseconds.</param>
/// <param name="ErrorMessage">Error details when <paramref name="Success"/> is <c>false</c>; otherwise <c>null</c>.</param>
/// <param name="TokenUsage">Token consumption breakdown for this request.</param>
public sealed record AiCompletionResult(
    bool Success,
    string Content,
    double DurationMs,
    string? ErrorMessage,
    TokenUsageInfo TokenUsage);

/// <summary>
/// Token consumption breakdown for a single AI request.
/// </summary>
public sealed record TokenUsageInfo(int InputTokens, int OutputTokens, int TotalTokens)
{
    public static readonly TokenUsageInfo Empty = new(0, 0, 0);
}

/// <summary>
/// Wraps <see cref="IChatCompletionService"/> and integrates metrics and
/// distributed-tracing internally so callers don't need to manage observability.
/// </summary>
public sealed class AiChatService
{
    private const string ModelId = "llama3.2";

    private readonly IChatCompletionService _chatCompletionService;
    private readonly AiMetricsService _metricsService;
    private readonly ILogger<AiChatService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AiChatService"/>.
    /// </summary>
    public AiChatService(
        IChatCompletionService chatCompletionService,
        AiMetricsService metricsService,
        ILogger<AiChatService> logger)
    {
        _chatCompletionService = chatCompletionService;
        _metricsService = metricsService;
        _logger = logger;
    }

    /// <summary>
    /// Sends the <paramref name="history"/> to the AI model and returns a completion result.
    /// This method never throws; errors are captured in the returned <see cref="AiCompletionResult"/>.
    /// </summary>
    public async Task<AiCompletionResult> GetCompletionAsync(
        ChatHistory history,
        string tenantId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        using var activity = AiMetricsService.ActivitySource.StartActivity("AI.ChatCompletion");

        var lastUserMessage = history.LastOrDefault(m => m.Role == AuthorRole.User);
        var promptLength = lastUserMessage?.Content?.Length ?? 0;

        activity?.SetTag("ai.model", ModelId);
        activity?.SetTag("ai.tenant", tenantId);
        activity?.SetTag("ai.user", userId);
        activity?.SetTag("ai.prompt.length", promptLength);
        activity?.SetTag("ai.history.count", history.Count);

        try
        {
            var response = await _chatCompletionService.GetChatMessageContentAsync(
                history,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            var content = response.Content ?? string.Empty;
            var tokenUsage = ExtractTokenUsage(response);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("ai.tokens.input", tokenUsage.InputTokens);
            activity?.SetTag("ai.tokens.output", tokenUsage.OutputTokens);
            activity?.SetTag("ai.tokens.total", tokenUsage.TotalTokens);

            _metricsService.RecordCall(new AiCallRecord
            {
                ModelId = ModelId,
                TenantId = tenantId,
                UserId = userId,
                Success = true,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                PromptLength = promptLength,
                ResponseLength = content.Length,
                HistoryMessageCount = history.Count,
                InputTokens = tokenUsage.InputTokens,
                OutputTokens = tokenUsage.OutputTokens,
                TotalTokens = tokenUsage.TotalTokens
            });

            _logger.LogInformation(
                "AI completion succeeded for tenant {TenantId}, user {UserId} in {DurationMs:F1} ms — tokens: {InputTokens} in / {OutputTokens} out / {TotalTokens} total",
                tenantId, userId, stopwatch.Elapsed.TotalMilliseconds,
                tokenUsage.InputTokens, tokenUsage.OutputTokens, tokenUsage.TotalTokens);

            return new AiCompletionResult(
                Success: true,
                Content: content,
                DurationMs: stopwatch.Elapsed.TotalMilliseconds,
                ErrorMessage: null,
                TokenUsage: tokenUsage);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _metricsService.RecordCall(new AiCallRecord
            {
                ModelId = ModelId,
                TenantId = tenantId,
                UserId = userId,
                Success = false,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                PromptLength = promptLength,
                ResponseLength = 0,
                ErrorMessage = ex.Message,
                HistoryMessageCount = history.Count,
                InputTokens = 0,
                OutputTokens = 0,
                TotalTokens = 0
            });

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(
                ex,
                "AI completion failed for tenant {TenantId}, user {UserId} after {DurationMs:F1} ms",
                tenantId, userId, stopwatch.Elapsed.TotalMilliseconds);

            var errorMessage = $"Error de comunicación con el modelo de IA local: {ex.Message}";

            return new AiCompletionResult(
                Success: false,
                Content: errorMessage,
                DurationMs: stopwatch.Elapsed.TotalMilliseconds,
                ErrorMessage: errorMessage,
                TokenUsage: TokenUsageInfo.Empty);
        }
    }

    /// <summary>
    /// Extracts token usage from the Semantic Kernel response metadata.
    /// Ollama returns prompt_eval_count (input) and eval_count (output) which SK
    /// surfaces through the Metadata dictionary or the InnerContent object.
    /// </summary>
    private TokenUsageInfo ExtractTokenUsage(ChatMessageContent response)
    {
        var metadata = response.Metadata;
        if (metadata is null)
            return TokenUsageInfo.Empty;

        int inputTokens = 0;
        int outputTokens = 0;

        // Strategy 1: SK may expose a "Usage" object with standard properties
        if (metadata.TryGetValue("Usage", out var usageObj) && usageObj is not null)
        {
            inputTokens = TryGetIntProperty(usageObj, "PromptTokens")
                       ?? TryGetIntProperty(usageObj, "InputTokens")
                       ?? 0;
            outputTokens = TryGetIntProperty(usageObj, "CompletionTokens")
                        ?? TryGetIntProperty(usageObj, "OutputTokens")
                        ?? 0;

            if (inputTokens > 0 || outputTokens > 0)
                return new TokenUsageInfo(inputTokens, outputTokens, inputTokens + outputTokens);
        }

        // Strategy 2: Ollama-specific flat metadata keys
        inputTokens = TryGetMetadataInt(metadata, "prompt_eval_count")
                   ?? TryGetMetadataInt(metadata, "PromptEvalCount")
                   ?? TryGetMetadataInt(metadata, "PromptTokenCount")
                   ?? 0;

        outputTokens = TryGetMetadataInt(metadata, "eval_count")
                    ?? TryGetMetadataInt(metadata, "EvalCount")
                    ?? TryGetMetadataInt(metadata, "CompletionTokenCount")
                    ?? 0;

        if (inputTokens > 0 || outputTokens > 0)
            return new TokenUsageInfo(inputTokens, outputTokens, inputTokens + outputTokens);

        // Strategy 3: Try extracting from InnerContent (OllamaSharp response object)
        var inner = response.InnerContent;
        if (inner is not null)
        {
            inputTokens = TryGetIntProperty(inner, "PromptEvalCount")
                       ?? TryGetIntProperty(inner, "prompt_eval_count")
                       ?? 0;
            outputTokens = TryGetIntProperty(inner, "EvalCount")
                        ?? TryGetIntProperty(inner, "eval_count")
                        ?? 0;

            if (inputTokens > 0 || outputTokens > 0)
                return new TokenUsageInfo(inputTokens, outputTokens, inputTokens + outputTokens);
        }

        _logger.LogDebug("Token usage not available in AI response metadata");
        return TokenUsageInfo.Empty;
    }

    /// <summary>
    /// Tries to read an int value from a metadata dictionary by key.
    /// </summary>
    private static int? TryGetMetadataInt(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    /// Tries to read an int property from an object via reflection (for InnerContent / Usage objects).
    /// </summary>
    private static int? TryGetIntProperty(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        if (prop is null)
            return null;

        var value = prop.GetValue(obj);
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            null => null,
            _ => null
        };
    }
}
