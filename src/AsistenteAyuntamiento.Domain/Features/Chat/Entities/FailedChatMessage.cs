namespace AsistenteAyuntamiento.Domain.Features.Chat.Entities;

/// <summary>
/// Dead-letter copy of a chat message that could not be saved (for example, its session was deleted meanwhile).
/// Kept for recovery by an operator; it has no tenant filter and is not exposed by any endpoint.
/// </summary>
public class FailedChatMessage
{
    /// <summary>Id of the original <see cref="ChatMessage"/>.</summary>
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }

    /// <summary>Exception type and database error code; never the message values.</summary>
    public string Error { get; set; } = string.Empty;
}
