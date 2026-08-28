namespace AsistenteAyuntamiento.ApiService.Features.Chat.Entities;

public class ChatSession
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
