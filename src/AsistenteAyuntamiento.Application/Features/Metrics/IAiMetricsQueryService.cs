using AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

namespace AsistenteAyuntamiento.Application.Features.Metrics;

/// <summary>Reads persisted AI usage. Chat data is scoped to the current tenant by the query filters.</summary>
public interface IAiMetricsQueryService
{
    Task<AiMetricsSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Most recent calls first. <paramref name="page"/> starts at 1; <paramref name="pageSize"/> is clamped to 1..100.</summary>
    Task<AiCallLogPageDto> GetHistoryAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
