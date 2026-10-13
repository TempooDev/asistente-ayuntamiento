using System.Text.Json.Serialization;

namespace AsistenteAyuntamiento.Application.Features.Arena.Models;

public class ArenaVoteResponse
{
    public string AlfaSystem { get; set; } = string.Empty;
    public string BetaSystem { get; set; } = string.Empty;

    /// <summary>Answer of the voted side (Alfa or Beta); <c>null</c> for ties and "both bad". Not sent to clients.</summary>
    [JsonIgnore]
    public string? WinningResponse { get; set; }
}
