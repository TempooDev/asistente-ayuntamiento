using System.Diagnostics;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>
/// Non-streaming calls to a platform model with the same span and metrics as the chat path (duration, tokens, cost,
/// tenant, pipeline, operation). Errors are recorded and rethrown, so callers keep their own handling.
/// </summary>
public static class MeteredChatCompletion
{
    public static Task<ChatMessageContent> CompleteMeteredAsync(
        this IChatCompletionService service,
        ChatHistory history,
        (string Provider, string Model) model,
        LlmCallScope scope,
        ILlmUsageMetrics metrics,
        PromptExecutionSettings? settings = null,
        CancellationToken cancellationToken = default) =>
        MeasureAsync(() => service.GetChatMessageContentAsync(history, settings, cancellationToken: cancellationToken), model, scope, metrics);

    /// <summary>Single-prompt overload; Semantic Kernel builds the history from the prompt exactly as before.</summary>
    public static Task<ChatMessageContent> CompleteMeteredAsync(
        this IChatCompletionService service,
        string prompt,
        (string Provider, string Model) model,
        LlmCallScope scope,
        ILlmUsageMetrics metrics,
        CancellationToken cancellationToken = default) =>
        MeasureAsync(() => service.GetChatMessageContentAsync(prompt, cancellationToken: cancellationToken), model, scope, metrics);

    private static async Task<ChatMessageContent> MeasureAsync(
        Func<Task<ChatMessageContent>> call,
        (string Provider, string Model) model,
        LlmCallScope scope,
        ILlmUsageMetrics metrics)
    {
        using var activity = AiMetricsService.ActivitySource.StartActivity("AI.ChatCompletion");
        activity?.SetTag(GenAiTelemetry.Attributes.OperationName, GenAiTelemetry.ChatOperation);
        activity?.SetTag(GenAiTelemetry.Attributes.ProviderName, GenAiTelemetry.ProviderName(model.Provider));
        activity?.SetTag(GenAiTelemetry.Attributes.RequestModel, model.Model);
        activity?.SetTag(GenAiTelemetry.Attributes.TenantId, scope.TenantId);
        activity?.SetTag(GenAiTelemetry.Attributes.Pipeline, scope.Pipeline);
        activity?.SetTag(GenAiTelemetry.Attributes.Operation, scope.Operation);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await call();
            var usage = TokenUsageExtractor.Extract(result);
            activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
            activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
            metrics.Record(new LlmCallMetrics(model.Provider, model.Model, scope.TenantId, scope.Pipeline, Fallback: false,
                stopwatch.Elapsed, usage.InputTokens, usage.OutputTokens, Operation: scope.Operation));
            return result;
        }
        catch (Exception ex)
        {
            var errorType = ex is OperationCanceledException ? "cancelled" : LlmErrorClassifier.Classify(ex);
            activity?.SetStatus(ActivityStatusCode.Error, errorType);
            metrics.Record(new LlmCallMetrics(model.Provider, model.Model, scope.TenantId, scope.Pipeline, Fallback: false,
                stopwatch.Elapsed, 0, 0, ErrorType: errorType, Operation: scope.Operation));
            throw;
        }
    }
}
