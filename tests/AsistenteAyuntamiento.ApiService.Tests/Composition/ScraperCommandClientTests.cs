using System.Net;
using AsistenteAyuntamiento.ApiService.Protos;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using Grpc.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.ApiService.Tests.Composition;

/// <summary>
/// ForceScrape is long and not idempotent: the scraper command client must not inherit the ServiceDefaults
/// resilience handler (10 s per attempt, retries).
/// </summary>
public class ScraperCommandClientTests
{
    [Fact]
    public async Task A_failed_ForceScrape_is_sent_once()
    {
        var calls = 0;
        await using var factory = new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(nameof(ScraperCommandService.ScraperCommandServiceClient))
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(() => Interlocked.Increment(ref calls)))));

        var client = factory.Services.GetRequiredService<ScraperCommandService.ScraperCommandServiceClient>();
        await Assert.ThrowsAsync<RpcException>(() => client.ForceScrapeAsync(new ForceScrapeRequest { Provider = "BOE" }).ResponseAsync);
        Assert.Equal(1, calls); // A retried ForceScrape would start a second scrape.
    }

    private sealed class StubHandler(Action onCall) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onCall();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Version = request.Version });
        }
    }
}
