namespace AsistenteAyuntamiento.Application.Features.Arena.Models;

public class ArenaAnalyticsResponse
{
    public int TotalBattles { get; set; }
    public int PendingBattles { get; set; }
    public int CompletedBattles { get; set; }
    public List<PipelineMetrics> Metrics { get; set; } = new();
}
