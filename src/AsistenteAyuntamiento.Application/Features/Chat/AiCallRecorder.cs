using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Domain.Features.Chat;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Application.Features.Chat;

public sealed class AiCallRecorder(
    IAppDbContext dbContext,
    IAiMetricsService metricsService,
    ILlmUsageMetrics usageMetrics,
    ILogger<AiCallRecorder> logger) : IAiCallRecorder
{
    public async Task RecordAsync(AiCallOutcome outcome, CancellationToken cancellationToken = default)
    {
        try
        {
            // Provider details stay server-side (AiCallLogs); metrics only carry the typed code.
            dbContext.AiCallLogs.Add(new AiCallLog
            {
                ModelId = outcome.ModelId,
                TenantId = outcome.TenantId,
                UserId = outcome.UserId,
                Success = outcome.Success,
                DurationMs = outcome.DurationMs,
                InputTokens = outcome.Usage.InputTokens,
                OutputTokens = outcome.Usage.OutputTokens,
                TotalTokens = outcome.Usage.TotalTokens,
                ErrorMessage = outcome.Success ? null : $"{outcome.ErrorCode}: {outcome.ErrorDetail}",
                Pipeline = outcome.Pipeline,
                Operation = outcome.Operation,
                Provider = string.IsNullOrEmpty(outcome.Provider) ? null : outcome.Provider,
                TimeToFirstTokenMs = outcome.TimeToFirstToken?.TotalMilliseconds
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save AiCallLog for tenant {TenantId}", outcome.TenantId);
        }

        usageMetrics.Record(new LlmCallMetrics(
            outcome.Provider,
            outcome.ModelId,
            outcome.TenantId,
            outcome.Pipeline,
            outcome.Fallback,
            TimeSpan.FromMilliseconds(outcome.DurationMs),
            outcome.Usage.InputTokens,
            outcome.Usage.OutputTokens,
            outcome.TimeToFirstToken,
            outcome.Success ? null : outcome.ErrorCode ?? LlmErrorCodes.Unavailable,
            outcome.Operation));

        // Legacy ai.* metrics and the /api/ai/metrics snapshot; the gen_ai.* metrics above replace them.
        metricsService.RecordCall(new AiCallRecord
        {
            ModelId = outcome.ModelId,
            TenantId = outcome.TenantId,
            UserId = outcome.UserId,
            Success = outcome.Success,
            DurationMs = outcome.DurationMs,
            PromptLength = outcome.PromptLength,
            ResponseLength = outcome.ResponseLength,
            ErrorMessage = outcome.ErrorCode,
            HistoryMessageCount = outcome.HistoryMessageCount,
            InputTokens = outcome.Usage.InputTokens,
            OutputTokens = outcome.Usage.OutputTokens,
            TotalTokens = outcome.Usage.TotalTokens
        });
    }
}
