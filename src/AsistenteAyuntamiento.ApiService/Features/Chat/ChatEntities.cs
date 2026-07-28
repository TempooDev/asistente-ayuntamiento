using System;
using System.Collections.Generic;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

public class ChatSession
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public ChatSession Session { get; set; } = null!;
    public string Role { get; set; } = string.Empty; // "user", "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
