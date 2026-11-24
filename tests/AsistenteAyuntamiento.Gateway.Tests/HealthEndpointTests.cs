using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AsistenteAyuntamiento.Gateway.Tests;

/// <summary>The gateway answers its probes in Production, not only in Development.</summary>
public sealed class HealthEndpointTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Answers_healthy_in_production(string path)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
