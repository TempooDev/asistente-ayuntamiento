using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;

namespace AsistenteAyuntamiento.Application.Features.AiConfig;

public interface IAiConfigurationService
{
    /// <summary>Chat model settings of the current tenant (platform defaults when it has none). Never exposes the key.</summary>
    Task<AiConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default);

    Task<string?> GetDecryptedApiKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Settings and decrypted key of <paramref name="explicitTenantId"/> (or the current tenant), falling back to the platform key.</summary>
    Task<(AiConfigurationDto Config, string? DecryptedApiKey)> GetFullConfigurationAsync(string? explicitTenantId = null, CancellationToken cancellationToken = default);

    Task SaveConfigurationAsync(SaveAiConfigurationDto dto, CancellationToken cancellationToken = default);
}
