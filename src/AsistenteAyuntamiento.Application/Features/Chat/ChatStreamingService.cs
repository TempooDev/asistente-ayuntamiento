using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

public sealed class ChatStreamingService(
    IAiConfigurationService aiConfigurationService,
    IChatModelFactory chatModelFactory,
    IAiCallRecorder callRecorder,
    ILogger<ChatStreamingService> logger) : IChatStreamingService
{
    internal const string FallbackNotice = "\n\n*(⚠️ Hemos alcanzado el límite de nuestra IA principal. Estamos usando un modelo de respaldo, por lo que las respuestas pueden ser más lentas o de menor calidad.)*";

    public async Task<AiCompletionResult> CompleteAsync(ChatHistory history, string tenantId, string userId, string pipeline, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = AiMetricsService.ActivitySource.StartActivity("AI.ChatCompletion");

        var (config, apiKey) = await aiConfigurationService.GetFullConfigurationAsync(tenantId, cancellationToken);
        var promptLength = LastUserMessageLength(history);
        Tag(activity, config, tenantId, userId, pipeline);
        activity?.SetTag("ai.prompt.length", promptLength);
        activity?.SetTag("ai.history.count", history.Count);

        try
        {
            var service = chatModelFactory.Create(config, apiKey);
            var settings = Settings(config);
            var modelId = config.Model;
            var provider = config.Provider;
            var usedFallback = false;

            ChatMessageContent response;
            try
            {
                response = await service.GetChatMessageContentAsync(history, settings, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (LlmErrorClassifier.IsQuotaExceeded(ex))
            {
                var fallback = chatModelFactory.CreateFallback();
                if (fallback is null)
                {
                    throw;
                }

                logger.LogWarning(ex, "Quota exceeded for tenant {TenantId}; using platform fallback model {FallbackModel}", tenantId, fallback.ModelId);
                modelId = fallback.ModelId;
                provider = fallback.Provider;
                usedFallback = true;
                MarkFallback(activity, fallback);
                response = await fallback.Service.GetChatMessageContentAsync(history, settings, cancellationToken: cancellationToken);
                response.Content += FallbackNotice;
            }

            stopwatch.Stop();
            var content = response.Content ?? string.Empty;
            var usage = TokenUsageExtractor.Extract(response);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
            activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);

            await callRecorder.RecordAsync(
                new AiCallOutcome(modelId, tenantId, userId, true, stopwatch.Elapsed.TotalMilliseconds, usage, promptLength, content.Length, history.Count)
                {
                    Provider = provider,
                    Pipeline = pipeline,
                    Fallback = usedFallback,
                },
                cancellationToken);

            logger.LogInformation(
                "AI completion succeeded for tenant {TenantId}, user {UserId} in {DurationMs:F1} ms — tokens: {InputTokens} in / {OutputTokens} out / {TotalTokens} total",
                tenantId, userId, stopwatch.Elapsed.TotalMilliseconds, usage.InputTokens, usage.OutputTokens, usage.TotalTokens);

            return new AiCompletionResult(true, content, stopwatch.Elapsed.TotalMilliseconds, null, usage, Sources: []);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorCode = LlmErrorClassifier.Classify(ex);

            // Provider details stay server-side (logs, AiCallLogs); the user only gets the typed message.
            await callRecorder.RecordAsync(
                new AiCallOutcome(config.Model, tenantId, userId, false, stopwatch.Elapsed.TotalMilliseconds, TokenUsageInfo.Empty, promptLength, 0, history.Count, errorCode, ex.Message)
                {
                    Provider = config.Provider,
                    Pipeline = pipeline,
                },
                CancellationToken.None);

            activity?.SetStatus(ActivityStatusCode.Error, errorCode);
            logger.LogError(ex, "AI completion failed ({ErrorCode}) for tenant {TenantId}, user {UserId} after {DurationMs:F1} ms",
                errorCode, tenantId, userId, stopwatch.Elapsed.TotalMilliseconds);

            var userMessage = LlmErrorCodes.UserMessage(errorCode);
            return new AiCompletionResult(false, userMessage, stopwatch.Elapsed.TotalMilliseconds, userMessage, TokenUsageInfo.Empty, ErrorCode: errorCode);
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatHistory history,
        string tenantId,
        string userId,
        string pipeline,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = AiMetricsService.ActivitySource.StartActivity("AI.ChatCompletion.Stream");

        var (config, apiKey) = await aiConfigurationService.GetFullConfigurationAsync(tenantId, cancellationToken);
        Tag(activity, config, tenantId, userId, pipeline);

        if (!TryCreateModel(config, apiKey, out var service, out var initError))
        {
            yield return initError;
            yield break;
        }

        var settings = Settings(config);
        var modelId = config.Model;
        var provider = config.Provider;
        TimeSpan? timeToFirstToken = null;
        var content = new StringBuilder();
        var usage = TokenUsageInfo.Empty;
        var usedFallback = false;
        string? errorText = null;
        string? errorCode = null;
        Exception? interruption = null;

        var enumerator = service.GetStreamingChatMessageContentsAsync(history, settings, cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                var (hasChunk, error) = await MoveNextAsync(enumerator);

                if (error is not null)
                {
                    if (content.Length == 0 && !usedFallback && LlmErrorClassifier.IsQuotaExceeded(error))
                    {
                        var fallback = chatModelFactory.CreateFallback();
                        if (fallback is null)
                        {
                            logger.LogWarning(error, "Quota exceeded for tenant {TenantId} and no fallback model is configured", tenantId);
                            errorCode = LlmErrorCodes.QuotaExceeded;
                            errorText = LlmErrorCodes.UserMessage(errorCode);
                            break;
                        }

                        logger.LogWarning(error, "Quota exceeded for tenant {TenantId}; streaming from platform fallback model {FallbackModel}", tenantId, fallback.ModelId);
                        usedFallback = true;
                        modelId = fallback.ModelId;
                        provider = fallback.Provider;
                        MarkFallback(activity, fallback);
                        await enumerator.DisposeAsync();
                        enumerator = fallback.Service.GetStreamingChatMessageContentsAsync(history, settings, cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);

                        var notice = FallbackNotice + "\n\n";
                        content.Append(notice);
                        yield return notice;
                        continue;
                    }

                    LogStreamFailure(error);
                    if (content.Length == 0)
                    {
                        errorCode = LlmErrorClassifier.Classify(error);
                        errorText = "\n\n" + LlmErrorCodes.UserMessage(errorCode);
                    }
                    else if (!cancellationToken.IsCancellationRequested)
                    {
                        // Text already sent cannot be replaced by a message: the stream ends with a typed error.
                        errorCode = LlmErrorClassifier.Classify(error);
                        interruption = error;
                    }
                    break;
                }

                if (!hasChunk)
                {
                    break;
                }

                var chunk = enumerator.Current;
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    if (timeToFirstToken is null)
                    {
                        timeToFirstToken = stopwatch.Elapsed;
                        activity?.AddEvent(new ActivityEvent("gen_ai.first_chunk"));
                    }
                    content.Append(chunk.Content);
                    yield return chunk.Content;
                }

                var chunkUsage = TokenUsageExtractor.Extract(chunk.Metadata, chunk.InnerContent);
                if (chunkUsage.TotalTokens > 0)
                {
                    usage = chunkUsage;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (errorText is not null)
        {
            content.Append(errorText);
            yield return errorText;
        }

        stopwatch.Stop();
        if (errorCode is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, errorCode);
        }
        activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
        activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);

        await callRecorder.RecordAsync(
            new AiCallOutcome(modelId, tenantId, userId, errorCode is null, stopwatch.Elapsed.TotalMilliseconds, usage,
                LastUserMessageLength(history), content.Length, history.Count, errorCode)
            {
                Provider = provider,
                Pipeline = pipeline,
                Fallback = usedFallback,
                TimeToFirstToken = timeToFirstToken,
            },
            CancellationToken.None);

        if (interruption is not null)
        {
            throw new LlmStreamInterruptedException(errorCode!, interruption);
        }
    }

    private bool TryCreateModel(AiConfigurationDto config, string? apiKey, out IChatCompletionService service, out string userError)
    {
        try
        {
            service = chatModelFactory.Create(config, apiKey);
            userError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            var errorCode = LlmErrorClassifier.Classify(ex);
            logger.LogError(ex, "Failed to initialize the chat model for streaming ({ErrorCode})", errorCode);
            service = null!;
            userError = LlmErrorCodes.UserMessage(errorCode);
            return false;
        }
    }

    private static async Task<(bool HasChunk, Exception? Error)> MoveNextAsync(IAsyncEnumerator<StreamingChatMessageContent> enumerator)
    {
        try
        {
            return (await enumerator.MoveNextAsync(), null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    private void LogStreamFailure(Exception error)
    {
        if (error is OperationCanceledException or IOException or System.Net.Sockets.SocketException || error.InnerException is OperationCanceledException)
        {
            logger.LogInformation("The AI stream was cancelled by the client or the network");
        }
        else
        {
            logger.LogWarning(error, "The AI stream failed");
        }
    }

    private static PromptExecutionSettings Settings(AiConfigurationDto config) => new()
    {
        ExtensionData = new Dictionary<string, object> { ["Temperature"] = config.Temperature }
    };

    private static int LastUserMessageLength(ChatHistory history) =>
        history.LastOrDefault(m => m.Role == AuthorRole.User)?.Content?.Length ?? 0;

    private static void Tag(Activity? activity, AiConfigurationDto config, string tenantId, string userId, string pipeline)
    {
        activity?.SetTag(GenAiTelemetry.Attributes.OperationName, GenAiTelemetry.ChatOperation);
        activity?.SetTag(GenAiTelemetry.Attributes.ProviderName, GenAiTelemetry.ProviderName(config.Provider));
        activity?.SetTag(GenAiTelemetry.Attributes.RequestModel, config.Model);
        activity?.SetTag(GenAiTelemetry.Attributes.TenantId, tenantId);
        activity?.SetTag(GenAiTelemetry.Attributes.Pipeline, pipeline);
        activity?.SetTag(GenAiTelemetry.Attributes.Fallback, false);
        activity?.SetTag("ai.user", userId);
    }

    private static void MarkFallback(Activity? activity, FallbackChatModel fallback)
    {
        activity?.SetTag(GenAiTelemetry.Attributes.Fallback, true);
        activity?.SetTag(GenAiTelemetry.Attributes.ProviderName, GenAiTelemetry.ProviderName(fallback.Provider));
        activity?.SetTag(GenAiTelemetry.Attributes.RequestModel, fallback.ModelId);
    }
}
