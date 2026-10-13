using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using AsistenteAyuntamiento.Gateway.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.Gateway.Tests;

/// <summary>Faro from the browser through the gateway (spec: observability-grafana, "Global Error Tracking").</summary>
public sealed class FaroForwardingTests : IAsyncLifetime
{
    private WebApplication _collector = null!;
    private string _collectorUrl = null!;
    private readonly ConcurrentQueue<ReceivedRequest> _received = new();

    private sealed record ReceivedRequest(string Path, string? Authorization, string? Cookie, string Body);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _collector = builder.Build();
        _collector.MapPost("/", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            _received.Enqueue(new ReceivedRequest(request.Path, request.Headers.Authorization, request.Headers.Cookie, await reader.ReadToEndAsync()));
            return Results.Accepted();
        });
        await _collector.StartAsync();
        _collectorUrl = _collector.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public async Task DisposeAsync() => await _collector.DisposeAsync();

    private static WebApplicationFactory<Program> Gateway(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        });

    [Fact]
    public async Task Faro_is_disabled_without_a_collector()
    {
        await using var gateway = Gateway();

        var config = await gateway.CreateClient().GetFromJsonAsync<FaroClientConfig>(FaroForwarding.ConfigPath);

        Assert.False(config!.Enabled);
        Assert.Null(config.CollectorUrl);
    }

    [Fact]
    public async Task Config_points_the_browser_to_the_same_origin_endpoint_without_secrets()
    {
        await using var gateway = Gateway(
            ("Telemetry:Faro:CollectorUrl", _collectorUrl),
            ("Telemetry:Faro:CollectorToken", "collector-secret"),
            ("Telemetry:Faro:Environment", "production"),
            ("Telemetry:Faro:SampleRate", "0.5"));

        var raw = await gateway.CreateClient().GetStringAsync(FaroForwarding.ConfigPath);
        var config = System.Text.Json.JsonSerializer.Deserialize<FaroClientConfig>(raw, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.True(config.Enabled);
        Assert.Equal(FaroForwarding.CollectPath, config.CollectorUrl);
        Assert.Equal("production", config.Environment);
        Assert.Equal(0.5, config.SampleRate);
        Assert.DoesNotContain("collector-secret", raw);
    }

    [Fact]
    public async Task Forwards_the_payload_with_the_collector_token_and_without_the_users_credentials()
    {
        await using var gateway = Gateway(("Telemetry:Faro:CollectorUrl", _collectorUrl), ("Telemetry:Faro:CollectorToken", "collector-secret"));
        using var request = new HttpRequestMessage(HttpMethod.Post, FaroForwarding.CollectPath)
        {
            Content = new StringContent("""{"meta":{},"exceptions":[]}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", "auth0.is.authenticated=true");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "user-access-token");

        var response = await gateway.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var received = Assert.Single(_received);
        Assert.Equal("/", received.Path);
        Assert.Equal("Bearer collector-secret", received.Authorization);
        Assert.Null(received.Cookie);
        Assert.Equal("""{"meta":{},"exceptions":[]}""", received.Body);
    }

    [Fact]
    public async Task Limits_the_requests_per_client()
    {
        await using var gateway = Gateway(("Telemetry:Faro:CollectorUrl", _collectorUrl));
        var client = gateway.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= FaroForwarding.PermitsPerMinute; i++)
        {
            using var response = await client.PostAsync(FaroForwarding.CollectPath, new StringContent("{}", Encoding.UTF8, "application/json"));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(FaroForwarding.PermitsPerMinute, statuses.Count(s => s == HttpStatusCode.Accepted));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Rejects_oversized_payloads()
    {
        await using var gateway = Gateway(("Telemetry:Faro:CollectorUrl", _collectorUrl));

        using var response = await gateway.CreateClient().PostAsync(FaroForwarding.CollectPath,
            new StringContent(new string('x', FaroForwarding.MaxBodyBytes + 1), Encoding.UTF8, "application/json"));

        Assert.NotEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(_received);
    }
}
