using AsistenteAyuntamiento.ApiService.Features.Tenants;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.ApiService.Features.AiConfig;

public class AiConfigurationService
{
    private readonly AppDbContext _dbContext;
    private readonly CurrentTenantService _tenantService;
    private readonly IDataProtector _dataProtector;

    public AiConfigurationService(AppDbContext dbContext, CurrentTenantService tenantService, IDataProtectionProvider dataProtectionProvider)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _dataProtector = dataProtectionProvider.CreateProtector("AiConfiguration.ApiKey");
    }

    public async Task<AiConfigurationDto> GetConfigurationAsync()
    {
        var tenantId = _tenantService.TenantId;
        var config = await _dbContext.AiConfigurations.FirstOrDefaultAsync(c => c.TenantId == tenantId);
        
        if (config == null)
        {
            return new AiConfigurationDto();
        }

        return new AiConfigurationDto
        {
            Provider = config.Provider,
            Model = config.Model,
            Temperature = config.Temperature,
            HasApiKey = !string.IsNullOrEmpty(config.EncryptedApiKey)
        };
    }

    public async Task<string?> GetDecryptedApiKeyAsync()
    {
        var tenantId = _tenantService.TenantId;
        var config = await _dbContext.AiConfigurations.FirstOrDefaultAsync(c => c.TenantId == tenantId);
        
        if (config == null || string.IsNullOrEmpty(config.EncryptedApiKey))
        {
            return null;
        }

        try
        {
            return _dataProtector.Unprotect(config.EncryptedApiKey);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(AiConfigurationDto Config, string? DecryptedApiKey)> GetFullConfigurationAsync()
    {
        var tenantId = _tenantService.TenantId;
        var config = await _dbContext.AiConfigurations.FirstOrDefaultAsync(c => c.TenantId == tenantId);
        
        if (config == null)
        {
            return (new AiConfigurationDto(), null);
        }

        var dto = new AiConfigurationDto
        {
            Provider = config.Provider,
            Model = config.Model,
            Temperature = config.Temperature,
            HasApiKey = !string.IsNullOrEmpty(config.EncryptedApiKey)
        };

        string? decryptedKey = null;
        if (!string.IsNullOrEmpty(config.EncryptedApiKey))
        {
            try
            {
                decryptedKey = _dataProtector.Unprotect(config.EncryptedApiKey);
            }
            catch
            {
                // ignore
            }
        }

        return (dto, decryptedKey);
    }

    public async Task SaveConfigurationAsync(SaveAiConfigurationDto dto)
    {
        var tenantId = _tenantService.TenantId;
        var config = await _dbContext.AiConfigurations.FirstOrDefaultAsync(c => c.TenantId == tenantId);
        
        if (config == null)
        {
            config = new AiConfiguration
            {
                TenantId = tenantId
            };
            _dbContext.AiConfigurations.Add(config);
        }

        config.Provider = dto.Provider;
        config.Model = dto.Model;
        config.Temperature = dto.Temperature;

        if (!string.IsNullOrEmpty(dto.ApiKey))
        {
            config.EncryptedApiKey = _dataProtector.Protect(dto.ApiKey);
        }
        // Si no envía ApiKey, se mantiene la existente.

        await _dbContext.SaveChangesAsync();
    }
}

public class AiConfigurationDto
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "llama3.2";
    public double Temperature { get; set; } = 0.3;
    public bool HasApiKey { get; set; }
}

public class SaveAiConfigurationDto
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "llama3.2";
    public double Temperature { get; set; } = 0.3;
    public string? ApiKey { get; set; }
}
