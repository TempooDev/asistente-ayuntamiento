using System.Net;
using AsistenteAyuntamiento.Infrastructure.Features.Ingestion;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace AsistenteAyuntamiento.Worker.Tests;

/// <summary>The Worker serves <c>/health</c> and <c>/alive</c> outside Development for container probes.</summary>
public class HealthEndpointTests(HealthEndpointTests.WorkerFactory factory) : IClassFixture<HealthEndpointTests.WorkerFactory>
{
    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task Health_endpoints_answer_in_production(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_probe_exits_with_zero_only_for_a_healthy_endpoint()
    {
        var handler = factory.Server.CreateHandler();

        Assert.Equal(0, await HealthProbe.RunAsync("http://localhost/health", handler));
        Assert.Equal(1, await HealthProbe.RunAsync("http://localhost/missing", handler));
        Assert.Equal(1, await HealthProbe.RunAsync("not a url"));
    }

    [Fact]
    public async Task Every_registration_resolves_in_development()
    {
        // Development validates the whole container when the app is built (ValidateOnBuild): a registration whose
        // dependencies are missing stops the Worker there, as it did when Data Protection was not registered.
        await using var development = new DevelopmentWorkerFactory();

        using var client = development.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
    }

    /// <summary>
    /// Boots the real Worker (Production by default) with no consumers running and only the "live" checks, so it
    /// needs no database, broker or vector store.
    /// </summary>
    public class WorkerFactory : WebApplicationFactory<Program>
    {
        protected virtual string Environment => Environments.Production;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environment);

            builder.UseSetting("ConnectionStrings:asistente-ayuntamiento-db", "Host=localhost;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("ConnectionStrings:messaging", "amqp://guest:guest@localhost:5672");
            builder.UseSetting("ConnectionStrings:qdrant", "Endpoint=http://localhost:6334;Key=unused");
            builder.UseSetting("Blob:Endpoint", "http://localhost:9000");
            builder.UseSetting("Blob:AccessKeyId", "test");
            builder.UseSetting("Blob:SecretAccessKey", "test");

            builder.ConfigureTestServices(services =>
            {
                var consumers = services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                             && (typeof(FlatChunkIngestionConsumer).IsAssignableFrom(d.ImplementationType) || d.ImplementationType == typeof(HierarchicalIngestionConsumer)))
                    .ToList();
                foreach (var descriptor in consumers)
                {
                    services.Remove(descriptor);
                }

                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    foreach (var registration in options.Registrations.Where(r => !r.Tags.Contains("live")).ToList())
                    {
                        options.Registrations.Remove(registration);
                    }
                });
            });
        }
    }

    public sealed class DevelopmentWorkerFactory : WorkerFactory
    {
        protected override string Environment => Environments.Development;
    }
}
