using AsistenteAyuntamiento.ApiService.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AsistenteAyuntamiento.ApiService.Tests.Security;

public class RequiredConfigurationTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static readonly Dictionary<string, string?> CompleteProduction = new()
    {
        ["Auth0:Domain"] = "tenant.eu.auth0.com",
        ["Auth0:Audience"] = "https://api",
        ["Auth0:CustomClaimsNamespace"] = "https://ns",
        ["Blob:Endpoint"] = "https://r2",
        ["Blob:AccessKeyId"] = "id",
        ["Blob:SecretAccessKey"] = "secret",
        ["Internal:GrpcApiKey"] = "key",
    };

    [Fact]
    public void Complete_production_configuration_is_valid()
    {
        Assert.Empty(RequiredConfiguration.FindMissing(Config(CompleteProduction), new FakeEnvironment(Environments.Production)));
    }

    [Theory]
    [InlineData("Blob:SecretAccessKey")]
    [InlineData("Internal:GrpcApiKey")]
    [InlineData("Auth0:Domain")]
    public void Missing_secret_fails_startup_in_production(string key)
    {
        var values = new Dictionary<string, string?>(CompleteProduction) { [key] = "" };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RequiredConfiguration.Validate(Config(values), new FakeEnvironment(Environments.Production)));

        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void Development_only_requires_auth0()
    {
        var values = new Dictionary<string, string?> { ["Auth0:Domain"] = "d", ["Auth0:Audience"] = "a" };

        Assert.Empty(RequiredConfiguration.FindMissing(Config(values), new FakeEnvironment(Environments.Development)));
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
