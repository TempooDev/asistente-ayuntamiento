namespace AsistenteAyuntamiento.Application.Features.Arena.Models;

public class ArenaVoteRequest
{
    public Guid SessionId { get; set; }
    public string Winner { get; set; } = string.Empty; // "ALFA", "BETA", or "TIE"
    public string? ClarityReason { get; set; }
    public string? PrecisionReason { get; set; }
    public string? OptionalComment { get; set; }
}
