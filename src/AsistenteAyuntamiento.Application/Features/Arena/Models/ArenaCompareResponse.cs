namespace AsistenteAyuntamiento.Application.Features.Arena.Models;

public class ArenaCompareResponse
{
    public Guid SessionId { get; set; }
    public string OptionAlfa { get; set; } = string.Empty;
    public string OptionBeta { get; set; } = string.Empty;
    public long LatencyAlfaMs { get; set; }
    public long LatencyBetaMs { get; set; }
}
