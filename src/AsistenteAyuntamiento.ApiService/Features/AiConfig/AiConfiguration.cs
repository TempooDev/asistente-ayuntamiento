using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.ApiService.Features.AiConfig;

public class AiConfiguration
{
    [Key]
    public string TenantId { get; set; } = null!;

    public string Provider { get; set; } = "ollama";
    
    public string Model { get; set; } = "llama3.2";
    
    public double Temperature { get; set; } = 0.3;
    
    // Almacenada de forma encriptada
    public string? EncryptedApiKey { get; set; }
}
