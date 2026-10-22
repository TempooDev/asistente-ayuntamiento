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
        { "GET", "/api/evaluation/runs" },
        { "GET", "/api/evaluation/runs/20261022-213000" },
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

    /// <summary>Route prefixes whose every mapped route must be classified in <see cref="RouteRules"/>.</summary>
    private static readonly string[] AdminAreas = ["/api/ingestion", "/api/scraper", "/api/settings", "/api/ai", "/api/arena", "/api/evaluation"];

    /// <summary>Expected authorization for routes that only require a signed-in user, not an admin role.</summary>
    private const string AuthenticatedUser = "(authenticated user)";

    /// <summary>Returned for a route in an admin area that no rule covers: the test fails until it is classified.</summary>
    private const string Unclassified = "(unclassified)";

    /// <summary>
    /// Expected authorization per route. An exact rule wins over a prefix rule, and the longest prefix wins. Mixed
    /// areas (<c>/api/arena</c>) have no prefix rule, so every new route there must be added explicitly.
    /// </summary>
    private static readonly (string Route, bool IsPrefix, string Expected)[] RouteRules =
    [
        ("/api/ingestion", true, Policies.PlatformAdmin),
        ("/api/scraper", true, Policies.PlatformAdmin),
        ("/api/evaluation", true, Policies.PlatformAdmin),
        ("/api/settings", true, Policies.OrganizationAdmin),
        ("/api/ai", true, Policies.PlatformAdmin),
        ("/api/ai/metrics/summary", false, Policies.OrganizationAdmin),
        ("/api/ai/metrics/history", false, Policies.OrganizationAdmin),
        ("/api/arena/analytics", false, Policies.PlatformAdmin),
        ("/api/arena/compare", false, Policies.OrganizationAdmin),
        ("/api/arena/vote", false, AuthenticatedUser),
    ];

    /// <summary>
    /// The authorization a route must have: a policy name, <see cref="AuthenticatedUser"/>, <see cref="Unclassified"/>,
    /// or null when the route is outside the admin areas.
    /// </summary>
    private static string? ExpectedAuthorization(string route)
    {
        route = route.TrimEnd('/');
        if (!AdminAreas.Any(area => IsUnder(route, area))) return null;

        var exact = RouteRules.FirstOrDefault(r => !r.IsPrefix && string.Equals(r.Route, route, StringComparison.OrdinalIgnoreCase));
        if (exact.Route is not null) return exact.Expected;

        var prefix = RouteRules
            .Where(r => r.IsPrefix && IsUnder(route, r.Route))
            .OrderByDescending(r => r.Route.Length)
            .FirstOrDefault();
        return prefix.Route is not null ? prefix.Expected : Unclassified;
    }

    private static bool IsUnder(string route, string prefix) =>
        string.Equals(route, prefix, StringComparison.OrdinalIgnoreCase)
        || route.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("/api/arena/new-admin-thing", Unclassified)]
    [InlineData("/api/arena", Unclassified)]
    [InlineData("/api/arena/vote", AuthenticatedUser)]
    [InlineData("/api/ai/new-thing", Policies.PlatformAdmin)]
    [InlineData("/api/ai/metrics/history", Policies.OrganizationAdmin)]
    [InlineData("/api/scraper/filters/{id:int}", Policies.PlatformAdmin)]
    [InlineData("/api/scraper/filters/", Policies.PlatformAdmin)]
    [InlineData("/api/arenas", null)]
    [InlineData("/api/users/me", null)]
    public void Route_classification_covers_new_routes(string route, string? expected)
    {
        Assert.Equal(expected, ExpectedAuthorization(route));
    }

    [Fact]
    public void Every_endpoint_under_an_admin_prefix_requires_its_admin_policy()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Route: e.RoutePattern.RawText ?? string.Empty, Endpoint: e))
            .Select(e => (e.Route, e.Endpoint, Expected: ExpectedAuthorization(e.Route)))
            .Where(e => e.Expected is not null)
            .ToList();

        // Every admin area is mapped, and every exact rule still matches a real route (no stale entries).
        Assert.All(AdminAreas, area => Assert.Contains(endpoints, e => IsUnder(e.Route.TrimEnd('/'), area)));
        Assert.All(RouteRules.Where(r => !r.IsPrefix), rule =>
            Assert.Contains(endpoints, e => string.Equals(e.Route.TrimEnd('/'), rule.Route, StringComparison.OrdinalIgnoreCase)));

        var wrong = new List<string>();
        foreach (var (route, endpoint, expected) in endpoints)
        {
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

            if (expected == Unclassified)
                wrong.Add($"{route}: not classified; add it to {nameof(RouteRules)} with its policy");
            else if (allowsAnonymous)
                wrong.Add($"{route}: allows anonymous access (expected {expected})");
            else if (expected == AuthenticatedUser && authorizeData.Count == 0)
                wrong.Add($"{route}: does not require authentication");
            else if (expected != AuthenticatedUser && !authorizeData.Any(a => a.Policy == expected))
                wrong.Add($"{route}: does not require {expected}");
        }

        Assert.Empty(wrong);
    }
}
