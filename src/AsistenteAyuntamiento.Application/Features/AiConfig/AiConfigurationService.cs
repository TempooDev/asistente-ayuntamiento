using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Application.Features.AiConfig;

/// <summary>
/// Per-tenant chat model settings. API keys are stored encrypted with Data Protection. Lookups filter by tenant
/// explicitly (allow-listed <c>IgnoreQueryFilters</c>) so background work can pass the tenant in.
/// </summary>
public class AiConfigurationService(
    IAppDbContext dbContext,
    ICurrentTenantService tenantService,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    ILogger<AiConfigurationService> logger) : IAiConfigurationService
{
    private readonly IDataProtector _dataProtector = dataProtectionProvider.CreateProtector("AiConfiguration.ApiKey");

    public async Task<AiConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await FindAsync(tenantService.TenantId, cancellationToken);
            return config is null ? PlatformDefaults() : ToDto(config);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error fetching the AI configuration of tenant {TenantId}; using platform defaults", tenantService.TenantId);
            return PlatformDefaults();
        }
    }

    public async Task<string?> GetDecryptedApiKeyAsync(CancellationToken cancellationToken = default)
    {
        var config = await FindAsync(tenantService.TenantId, cancellationToken);
        return TryUnprotect(config?.EncryptedApiKey);
    }

    /// <summary>
    /// The tenant's settings with its own key, or the platform's provider, model, endpoint and key together. The
    /// platform key is never paired with a tenant-chosen provider or endpoint, so an organization administrator
    /// cannot point it at a server of their own. Ollama needs no key (and always uses the platform's Ollama).
    /// </summary>
    public async Task<(AiConfigurationDto Config, string? DecryptedApiKey)> GetFullConfigurationAsync(string? explicitTenantId = null, CancellationToken cancellationToken = default)
    {
        var tenantId = explicitTenantId ?? tenantService.TenantId;
        var config = await FindAsync(tenantId, cancellationToken);
        if (config is null)
        {
            return (PlatformDefaults(), configuration["Ai:Chat:ApiKey"]);
        }

        var apiKey = TryUnprotect(config.EncryptedApiKey);
        if (!string.IsNullOrEmpty(apiKey))
        {
            return (ToDto(config), apiKey);
        }

        if (IsKeyless(config.Provider))
        {
            return (ToDto(config), null);
        }

        logger.LogWarning("Tenant {TenantId} chose provider {Provider} without a usable API key; using the platform model", tenantId, config.Provider);
        return (PlatformDefaults(), configuration["Ai:Chat:ApiKey"]);
    }

    public async Task SaveConfigurationAsync(SaveAiConfigurationDto dto, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantService.TenantId
            ?? throw new InvalidOperationException("A tenant is required to save the AI configuration.");
        var config = await dbContext.AiConfigurations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config is null)
        {
            config = new AiConfiguration { TenantId = tenantId };
            dbContext.AiConfigurations.Add(config);
        }

        config.Provider = dto.Provider ?? "";
        config.Model = dto.Model ?? "";
        config.Temperature = dto.Temperature;
        config.EndpointUrl = dto.EndpointUrl;

        // An empty key keeps the stored one.
        if (!string.IsNullOrEmpty(dto.ApiKey))
        {
            config.EncryptedApiKey = _dataProtector.Protect(dto.ApiKey);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<AiConfiguration?> FindAsync(string? tenantId, CancellationToken cancellationToken) =>
        dbContext.AiConfigurations.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

    private static bool IsKeyless(string? provider) =>
        string.IsNullOrEmpty(provider) || provider.Equals("ollama", StringComparison.OrdinalIgnoreCase);

    private AiConfigurationDto PlatformDefaults() => new()
    {
        Provider = configuration["Ai:Chat:Provider"] ?? "ollama",
        Model = configuration["Ai:Chat:Model"] ?? "llama3.2",
        Temperature = 0.3,
        HasApiKey = !string.IsNullOrEmpty(configuration["Ai:Chat:ApiKey"]),
        EndpointUrl = configuration["Ai:Chat:EndpointUrl"]
    };

    private static AiConfigurationDto ToDto(AiConfiguration config) => new()
    {
        Provider = config.Provider,
        Model = config.Model,
        Temperature = config.Temperature,
        HasApiKey = !string.IsNullOrEmpty(config.EncryptedApiKey),
        EndpointUrl = config.EndpointUrl
    };

    /// <summary>Null when there is no key or it cannot be decrypted (for example after a key-ring change).</summary>
    private string? TryUnprotect(string? encryptedApiKey)
    {
        if (string.IsNullOrEmpty(encryptedApiKey))
        {
            return null;
        }

        try
        {
            return _dataProtector.Unprotect(encryptedApiKey);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            logger.LogWarning(ex, "Stored API key could not be decrypted");
            return null;
        }
    }
}
