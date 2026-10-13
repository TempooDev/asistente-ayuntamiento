using System.Net;
using System.Net.Http.Json;
using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.ApiService.Tests.Security;

/// <summary>
/// Authorization matrix for administrative APIs (spec: api-authorization): platform routes (shared knowledge base,
/// cross-tenant data) and organization routes (one tenant).
/// </summary>
public class AdminAuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Org = "org_A";

    /// <summary>Shared knowledge base and cross-tenant data, including the destructive operations.</summary>
    public static TheoryData<string, string> PlatformRoutes => new()
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
        { "GET", "/api/ai/metrics" },
        { "GET", "/api/ai/health" },
        { "GET", "/api/ai/metrics/ingestion" },
        { "GET", "/api/arena/analytics" },
    };

    /// <summary>Settings and data of the caller's organization.</summary>
    public static TheoryData<string, string> OrganizationRoutes => new()
    {
        { "GET", "/api/settings/ai" },
        { "PUT", "/api/settings/ai" },
        { "GET", "/api/ai/metrics/summary" },
        { "GET", "/api/ai/metrics/history" },
        { "POST", "/api/arena/compare" },
    };

    public static TheoryData<string, string> AllAdminRoutes
    {
        get
        {
            var all = new TheoryData<string, string>();
            foreach (var row in PlatformRoutes.Concat(OrganizationRoutes)) all.Add((string)row[0], (string)row[1]);
            return all;
        }
    }

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path)
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null
        };

    [Theory]
    [MemberData(nameof(AllAdminRoutes))]
    public async Task Anonymous_request_to_admin_endpoint_is_unauthorized(string method, string path)
    {
        var response = await factory.CreateClientAs().SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AllAdminRoutes))]
    public async Task Authenticated_non_admin_request_to_admin_endpoint_is_forbidden(string method, string path)
    {
        var response = await factory.CreateClientAs("auth0|user", Org, "Funcionario").SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(PlatformRoutes))]
    public async Task Organization_admin_cannot_use_platform_routes(string method, string path)
    {
        var response = await factory.CreateClientAs("auth0|org-admin", Org, Roles.OrganizationAdmin).SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OrganizationRoutes))]
    public async Task Platform_admin_cannot_use_organization_routes(string method, string path)
    {
        var response = await factory.CreateClientAs("auth0|platform-admin", Org, Roles.PlatformAdmin).SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.PlatformAdmin, "/api/scraper/filters")]
    [InlineData(Roles.PlatformAdmin, "/api/ai/metrics")]
    [InlineData(Roles.PlatformAdmin, "/api/ai/metrics/ingestion")]
    [InlineData(Roles.PlatformAdmin, "/api/arena/analytics")]
    [InlineData(Roles.OrganizationAdmin, "/api/settings/ai")]
    [InlineData(Roles.OrganizationAdmin, "/api/ai/metrics/history")]
    public async Task Each_admin_role_can_use_its_routes(string role, string path)
    {
        var response = await factory.CreateClientAs($"auth0|{role}", Org, role).GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/scraper/filters")]
    [InlineData("/api/settings/ai")]
    public async Task The_retired_administrador_role_no_longer_grants_access(string path)
    {
        var response = await factory.CreateClientAs("auth0|old-admin", Org, "administrador").GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Role_check_is_case_insensitive_like_the_spa_guard()
    {
        var response = await factory.CreateClientAs("auth0|admin", Org, "Admin-Plataforma").GetAsync("/api/scraper/filters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Every_endpoint_under_an_admin_prefix_requires_its_admin_policy()
    {
        (string Prefix, string Policy)[] expected =
        [
            ("/api/ingestion", Policies.PlatformAdmin),
            ("/api/scraper", Policies.PlatformAdmin),
            ("/api/arena/analytics", Policies.PlatformAdmin),
            ("/api/ai/metrics/summary", Policies.OrganizationAdmin),
            ("/api/ai/metrics/history", Policies.OrganizationAdmin),
            ("/api/ai/", Policies.PlatformAdmin),
            ("/api/settings", Policies.OrganizationAdmin),
            ("/api/arena/compare", Policies.OrganizationAdmin),
        ];

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Route: e.RoutePattern.RawText ?? string.Empty, Endpoint: e))
            .Select(e => (e.Route, e.Endpoint, Expected: expected.FirstOrDefault(x => e.Route.StartsWith(x.Prefix, StringComparison.OrdinalIgnoreCase)).Policy))
            .Where(e => e.Expected is not null)
            .ToList();

        Assert.NotEmpty(endpoints);

        var wrong = endpoints
            .Where(e => !e.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == e.Expected))
            .Select(e => $"{e.Route} (expected {e.Expected})")
            .ToList();

        Assert.Empty(wrong);
    }
}
