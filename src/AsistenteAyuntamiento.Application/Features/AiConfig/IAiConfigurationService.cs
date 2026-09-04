using System.Threading.Tasks;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;

namespace AsistenteAyuntamiento.Application.Features.AiConfig;

public interface IAiConfigurationService
{
    Task<AiConfigurationDto> GetConfigurationAsync();
    Task<string?> GetDecryptedApiKeyAsync();
    Task<(AiConfigurationDto Config, string? DecryptedApiKey)> GetFullConfigurationAsync(string? explicitTenantId = null);
    Task SaveConfigurationAsync(SaveAiConfigurationDto dto);
}
