namespace AsistenteAyuntamiento.Application.Features.Arena.Models;

public class PipelineMetrics
{
    public string Pipeline { get; set; } = string.Empty;
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public int TieCount { get; set; }
    public int BothBadCount { get; set; }

    /// <summary>Wins / (wins + losses): ties, "both bad" and pending battles are not in the denominator.</summary>
    public double WinRate { get; set; }
    public double AverageLatencyMs { get; set; }
    public double AverageTokens { get; set; }
}
