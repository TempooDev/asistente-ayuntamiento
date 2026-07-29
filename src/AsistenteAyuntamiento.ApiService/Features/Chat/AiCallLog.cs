using System;
using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

public class AiCallLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [Required]
    [MaxLength(100)]
    public string TenantId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string ModelId { get; set; } = string.Empty;
    
    public bool Success { get; set; }
    
    public double DurationMs { get; set; }
    
    public int InputTokens { get; set; }
    
    public int OutputTokens { get; set; }
    
    public int TotalTokens { get; set; }
    
    public string? ErrorMessage { get; set; }
}
