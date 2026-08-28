namespace AsistenteAyuntamiento.ApiService.Features.AiConfig.DTOs;

public class SaveAiConfigurationDto
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "llama3.2";
    public double Temperature { get; set; } = 0.3;
    public string? ApiKey { get; set; }
    public string? EndpointUrl { get; set; }
}
