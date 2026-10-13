using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MockQueryable.Moq;
using Moq;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.AiConfig;

public class AiConfigurationServiceTests
{
    private readonly Mock<IAppDbContext> _dbContextMock;
    private readonly Mock<ICurrentTenantService> _tenantServiceMock;
    private readonly Mock<IDataProtectionProvider> _dataProtectionProviderMock;
    private readonly Mock<IDataProtector> _dataProtectorMock;
    private readonly IConfiguration _configuration;
    private readonly AiConfigurationService _sut;

    public AiConfigurationServiceTests()
    {
        _dbContextMock = new Mock<IAppDbContext>();
        _tenantServiceMock = new Mock<ICurrentTenantService>();
        _dataProtectionProviderMock = new Mock<IDataProtectionProvider>();
        _dataProtectorMock = new Mock<IDataProtector>();

        _dataProtectionProviderMock.Setup(p => p.CreateProtector(It.IsAny<string>()))
                                   .Returns(_dataProtectorMock.Object);

        var inMemorySettings = new Dictionary<string, string?> {
            {"Ai:Chat:Provider", "default-provider"},
            {"Ai:Chat:Model", "default-model"},
            {"Ai:Chat:ApiKey", "default-api-key"},
            {"Ai:Chat:EndpointUrl", "http://default.url"}
        };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        _sut = new AiConfigurationService(_dbContextMock.Object, _tenantServiceMock.Object, _dataProtectionProviderMock.Object, _configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiConfigurationService>.Instance);
    }

    [Fact]
    public async Task GetConfigurationAsync_ShouldReturnDefaultConfig_WhenDbIsEmpty()
    {
        // Arrange
        _tenantServiceMock.Setup(t => t.TenantId).Returns("tenant1");

        var emptyData = new List<AiConfiguration>().BuildMockDbSet();
        _dbContextMock.Setup(db => db.AiConfigurations).Returns(emptyData.Object);

        // Act
        var result = await _sut.GetConfigurationAsync();

        // Assert
        result.Should().NotBeNull();
        result.Provider.Should().Be("default-provider");
        result.Model.Should().Be("default-model");
        result.HasApiKey.Should().BeTrue();
        result.EndpointUrl.Should().Be("http://default.url");
    }

    [Fact]
    public async Task GetConfigurationAsync_ShouldReturnDbConfig_WhenDbHasConfigForTenant()
    {
        // Arrange
        _tenantServiceMock.Setup(t => t.TenantId).Returns("tenant1");

        var data = new List<AiConfiguration>
        {
            new AiConfiguration
            {
                TenantId = "tenant1",
                Provider = "db-provider",
                Model = "db-model",
                Temperature = 0.8,
                EncryptedApiKey = "YmFzZTY0dXJsZW5jb2RlZA",
                EndpointUrl = "http://db.url"
            }
        }.BuildMockDbSet();

        _dbContextMock.Setup(db => db.AiConfigurations).Returns(data.Object);

        // Act
        var result = await _sut.GetConfigurationAsync();

        // Assert
        result.Should().NotBeNull();
        result.Provider.Should().Be("db-provider");
        result.Model.Should().Be("db-model");
        result.Temperature.Should().Be(0.8);
        result.HasApiKey.Should().BeTrue();
        result.EndpointUrl.Should().Be("http://db.url");
    }

    [Fact]
    public async Task GetDecryptedApiKeyAsync_ShouldReturnNull_WhenDbIsEmpty()
    {
        // Arrange
        _tenantServiceMock.Setup(t => t.TenantId).Returns("tenant1");
        var emptyData = new List<AiConfiguration>().BuildMockDbSet();
        _dbContextMock.Setup(db => db.AiConfigurations).Returns(emptyData.Object);

        // Act
        var result = await _sut.GetDecryptedApiKeyAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDecryptedApiKeyAsync_ShouldReturnDecryptedKey_WhenKeyExists()
    {
        // Arrange
        _tenantServiceMock.Setup(t => t.TenantId).Returns("tenant1");

        var data = new List<AiConfiguration>
        {
            new AiConfiguration
            {
                TenantId = "tenant1",
                EncryptedApiKey = "YmFzZTY0dXJsZW5jb2RlZA"
            }
        }.BuildMockDbSet();

        _dbContextMock.Setup(db => db.AiConfigurations).Returns(data.Object);
        _dataProtectorMock.Setup(p => p.Unprotect(It.IsAny<byte[]>()))
                          .Returns(System.Text.Encoding.UTF8.GetBytes("decrypted-key"));

        // Act
        var result = await _sut.GetDecryptedApiKeyAsync();

        // Assert
        result.Should().Be("decrypted-key");
    }

    private void GivenTenantConfig(string provider, string? encryptedKey)
    {
        _tenantServiceMock.Setup(t => t.TenantId).Returns("tenant1");
        var data = new List<AiConfiguration>
        {
            new() { TenantId = "tenant1", Provider = provider, Model = "tenant-model", EncryptedApiKey = encryptedKey, EndpointUrl = "https://attacker.example" }
        }.BuildMockDbSet();
        _dbContextMock.Setup(db => db.AiConfigurations).Returns(data.Object);
    }

    [Fact]
    public async Task GetFullConfigurationAsync_WithoutTenantKey_UsesPlatformProviderEndpointAndKeyTogether()
    {
        GivenTenantConfig("openai", encryptedKey: null);

        var (config, apiKey) = await _sut.GetFullConfigurationAsync();

        apiKey.Should().Be("default-api-key");
        config.Provider.Should().Be("default-provider");
        config.Model.Should().Be("default-model");
        config.EndpointUrl.Should().Be("http://default.url");
    }

    [Fact]
    public async Task GetFullConfigurationAsync_WhenTenantKeyCannotBeDecrypted_UsesPlatformSettings()
    {
        GivenTenantConfig("openai", encryptedKey: "YmFzZTY0dXJsZW5jb2RlZA");
        _dataProtectorMock.Setup(p => p.Unprotect(It.IsAny<byte[]>()))
                          .Throws(new System.Security.Cryptography.CryptographicException());

        var (config, apiKey) = await _sut.GetFullConfigurationAsync();

        apiKey.Should().Be("default-api-key");
        config.EndpointUrl.Should().Be("http://default.url");
    }

    [Fact]
    public async Task GetFullConfigurationAsync_WithTenantKey_UsesTenantSettings()
    {
        GivenTenantConfig("openai", encryptedKey: "YmFzZTY0dXJsZW5jb2RlZA");
        _dataProtectorMock.Setup(p => p.Unprotect(It.IsAny<byte[]>()))
                          .Returns(System.Text.Encoding.UTF8.GetBytes("tenant-key"));

        var (config, apiKey) = await _sut.GetFullConfigurationAsync();

        apiKey.Should().Be("tenant-key");
        config.Model.Should().Be("tenant-model");
        config.EndpointUrl.Should().Be("https://attacker.example");
    }

    [Fact]
    public async Task GetFullConfigurationAsync_OllamaWithoutKey_KeepsTenantModelAndSendsNoKey()
    {
        GivenTenantConfig("ollama", encryptedKey: null);

        var (config, apiKey) = await _sut.GetFullConfigurationAsync();

        apiKey.Should().BeNull();
        config.Model.Should().Be("tenant-model");
    }
}
