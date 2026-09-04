using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Arena.Models;

public class PipelineMetrics
{
    public string Pipeline { get; set; } = string.Empty;
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public int TieCount { get; set; }
    public double WinRate { get; set; }
    public double AverageLatencyMs { get; set; }
    public double AverageTokens { get; set; }
}

public class ArenaAnalyticsResponse
{
    public int TotalBattles { get; set; }
    public int PendingBattles { get; set; }
    public int CompletedBattles { get; set; }
    public List<PipelineMetrics> Metrics { get; set; } = new();
}
