using System.Diagnostics;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>
/// Non-streaming calls to a platform model with the same span and metrics as the chat path (duration, tokens, cost,
/// tenant, pipeline, operation). Errors are recorded and rethrown, so callers keep their own handling. When the scope
/// names a user and a <see cref="IAiCallRecorder"/> is given, the call is also stored in <c>AiCallLogs</c> (the
/// recorder emits the metrics too).
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
        CancellationToken cancellationToken = default,
        IAiCallRecorder? recorder = null) =>
        MeasureAsync(() => service.GetChatMessageContentAsync(history, settings, cancellationToken: cancellationToken), model, scope, metrics,
            recorder, history.LastOrDefault(m => m.Role == AuthorRole.User)?.Content?.Length ?? 0, history.Count);

    /// <summary>Single-prompt overload; Semantic Kernel builds the history from the prompt exactly as before.</summary>
    public static Task<ChatMessageContent> CompleteMeteredAsync(
        this IChatCompletionService service,
        string prompt,
        (string Provider, string Model) model,
        LlmCallScope scope,
        ILlmUsageMetrics metrics,
        CancellationToken cancellationToken = default,
        IAiCallRecorder? recorder = null) =>
        MeasureAsync(() => service.GetChatMessageContentAsync(prompt, cancellationToken: cancellationToken), model, scope, metrics,
            recorder, prompt.Length, 1);

    private static async Task<ChatMessageContent> MeasureAsync(
        Func<Task<ChatMessageContent>> call,
        (string Provider, string Model) model,
        LlmCallScope scope,
        ILlmUsageMetrics metrics,
        IAiCallRecorder? recorder,
        int promptLength,
        int historyCount)
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
            await RecordAsync(true, usage, result.Content?.Length ?? 0, null, null);
            return result;
        }
        catch (Exception ex)
        {
            var errorType = ex is OperationCanceledException ? "cancelled" : LlmErrorClassifier.Classify(ex);
            activity?.SetStatus(ActivityStatusCode.Error, errorType);
            await RecordAsync(false, TokenUsageInfo.Empty, 0, errorType, ex.Message);
            throw;
        }

        async Task RecordAsync(bool success, TokenUsageInfo usage, int responseLength, string? errorType, string? errorDetail)
        {
            if (recorder is null || scope.UserId is null)
            {
                metrics.Record(new LlmCallMetrics(model.Provider, model.Model, scope.TenantId, scope.Pipeline, Fallback: false,
                    stopwatch.Elapsed, usage.InputTokens, usage.OutputTokens, ErrorType: errorType, Operation: scope.Operation));
                return;
            }

            await recorder.RecordAsync(
                new AiCallOutcome(model.Model, scope.TenantId, scope.UserId, success, stopwatch.Elapsed.TotalMilliseconds, usage,
                    promptLength, responseLength, historyCount, errorType, errorDetail)
                {
                    Provider = model.Provider,
                    Pipeline = scope.Pipeline,
                    Operation = scope.Operation,
                },
                CancellationToken.None);
        }
    }
}
