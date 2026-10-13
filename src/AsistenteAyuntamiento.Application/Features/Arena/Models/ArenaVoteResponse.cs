using System.Text.Json.Serialization;

namespace AsistenteAyuntamiento.Application.Features.Arena.Models;

public class ArenaVoteResponse
{
    public string AlfaSystem { get; set; } = string.Empty;
    public string BetaSystem { get; set; } = string.Empty;

    /// <summary>
    /// Answer the chat keeps after the vote: the voted side for Alfa or Beta, both answers for a tie, <c>null</c> for
    /// "both bad". Not sent to REST clients.
    /// </summary>
    [JsonIgnore]
    public string? ChatAnswer { get; set; }
}
