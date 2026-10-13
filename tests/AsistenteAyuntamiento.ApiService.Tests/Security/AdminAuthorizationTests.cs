using System.Net;
using System.Net.Http.Json;
using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.ApiService.Tests.Security;

/// <summary>
/// Authorization matrix for administrative APIs (spec: api-authorization).
/// </summary>
public class AdminAuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Org = "org_A";

    /// <summary>Every administrative operation, including the destructive ones.</summary>
    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/ingestion/blobs" },
        { "POST", "/api/ingestion/process-blob" },
        { "POST", "/api/ingestion/reset" },
        { "POST", "/api/ingestion/reset-status/doc-1" },
        { "POST", "/api/ingestion/reset-stuck-processing" },
        { "POST", "/api/ingestion/enqueue-bulk" },
        { "POST", "/api/ingestion/reprocess-all" },
        { "GET", "/api/scraper/filters" },
        { "POST", "/api/scraper/filters" },
        { "DELETE", "/api/scraper/filters/1" },
        { "POST", "/api/scraper/filters/trigger" },
        { "GET", "/api/scraper/filters/state" },
        { "GET", "/api/settings/ai" },
        { "PUT", "/api/settings/ai" },
        { "GET", "/api/ai/metrics" },
        { "GET", "/api/ai/metrics/summary" },
        { "GET", "/api/ai/metrics/history" },
        { "GET", "/api/ai/health" },
        { "GET", "/api/arena/analytics" },
        { "POST", "/api/arena/compare" },
    };

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path)
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null
        };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Anonymous_request_to_admin_endpoint_is_unauthorized(string method, string path)
    {
        var client = factory.CreateClientAs();

        var response = await client.SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Authenticated_non_admin_request_to_admin_endpoint_is_forbidden(string method, string path)
    {
        var client = factory.CreateClientAs("auth0|user", Org, "Funcionario");

        var response = await client.SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/scraper/filters")]
    [InlineData("/api/settings/ai")]
    [InlineData("/api/ai/metrics")]
    [InlineData("/api/ai/metrics/history")]
    [InlineData("/api/arena/analytics")]
    public async Task Admin_can_call_admin_endpoint(string path)
    {
        var client = factory.CreateClientAs("auth0|admin", Org, Roles.Admin);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_role_check_is_case_insensitive_like_the_spa_guard()
    {
        var client = factory.CreateClientAs("auth0|admin", Org, "Administrador");

        var response = await client.GetAsync("/api/scraper/filters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Every_endpoint_under_an_admin_prefix_requires_the_admin_policy()
    {
        string[] adminPrefixes = ["/api/ingestion", "/api/scraper", "/api/settings", "/api/ai/", "/api/arena/analytics", "/api/arena/compare"];

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } route && adminPrefixes.Any(p => route.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.NotEmpty(endpoints);

        var unprotected = endpoints
            .Where(e => !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == Policies.Admin))
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        Assert.Empty(unprotected);
    }
}
