using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Chat;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Application.Features.Chat;

public sealed class AiCallRecorder(IAppDbContext dbContext, IAiMetricsService metricsService, ILogger<AiCallRecorder> logger) : IAiCallRecorder
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
                ErrorMessage = outcome.Success ? null : $"{outcome.ErrorCode}: {outcome.ErrorDetail}"
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save AiCallLog for tenant {TenantId}", outcome.TenantId);
        }

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
