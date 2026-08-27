using AsistenteAyuntamiento.ApiService.Features.Tenants;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.ApiService.Features.AiConfig;

public class AiConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _dbContext;
    private readonly CurrentTenantService _tenantService;
    private readonly IDataProtector _dataProtector;

    public AiConfigurationService(AppDbContext dbContext, CurrentTenantService tenantService, IDataProtectionProvider dataProtectionProvider, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _dataProtector = dataProtectionProvider.CreateProtector("AiConfiguration.ApiKey");
        _configuration = configuration;
    }

    public async Task<AiConfigurationDto> GetConfigurationAsync()
    {
        var tenantId = _tenantService.TenantId;
        var config = await _dbContext.AiConfigurations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId);
        
        var defaultConfig = _configuration; // IConfiguration injected

        if (config == null)
        {
            return new AiConfigurationDto
            {
                Provider = _configuration["Ai:Chat:Provider"] ?? "ollama",
                Model = _configuration["Ai:Chat:Model"] ?? "llama3.2",
                Temperature = 0.3,
                HasApiKey = !string.IsNullOrEmpty(_configuration["Ai:Chat:ApiKey"]),
                EndpointUrl = _configuration["Ai:Chat:EndpointUrl"]
            };
        }

        return new AiConfigurationDto
        {
            Provider = config.Provider,
            Model = config.Model,
            Temperature = config.Temperature,
            HasApiKey = !string.IsNullOrEmpty(config.EncryptedApiKey),
            EndpointUrl = config.EndpointUrl
        };
    }

    public async Task<string?> GetDecryptedApiKeyAsync()
    {
        var tenantId = _tenantService.TenantId;
        var config = await _dbContext.AiConfigurations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId);
        
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

    public async Task<(AiConfigurationDto Config, string? DecryptedApiKey)> GetFullConfigurationAsync(string? explicitTenantId = null)
    {
        var tenantId = explicitTenantId ?? _tenantService.TenantId;
        var config = await _dbContext.AiConfigurations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId);
        
        if (config == null)
        {
            var defaultConfig = new AiConfigurationDto
            {
                Provider = _configuration["Ai:Chat:Provider"] ?? "ollama",
                Model = _configuration["Ai:Chat:Model"] ?? "llama3.2",
                Temperature = 0.3,
                HasApiKey = !string.IsNullOrEmpty(_configuration["Ai:Chat:ApiKey"]),
                EndpointUrl = _configuration["Ai:Chat:EndpointUrl"]
            };
            return (defaultConfig, _configuration["Ai:Chat:ApiKey"]);
        }

        var dto = new AiConfigurationDto
        {
            Provider = config.Provider,
            Model = config.Model,
            Temperature = config.Temperature,
            HasApiKey = !string.IsNullOrEmpty(config.EncryptedApiKey),
            EndpointUrl = config.EndpointUrl
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
        var config = await _dbContext.AiConfigurations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId);
        
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
        config.EndpointUrl = dto.EndpointUrl;

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
    public string? EndpointUrl { get; set; }
}

public class SaveAiConfigurationDto
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "llama3.2";
    public double Temperature { get; set; } = 0.3;
    public string? ApiKey { get; set; }
    public string? EndpointUrl { get; set; }
}
