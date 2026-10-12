using System.Security.Claims;
using AsistenteAyuntamiento.ApiService.Features.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AsistenteAyuntamiento.ApiService.Tests.Tenancy;

public class CurrentTenantServiceTests
{
    private static CurrentTenantService Create(string environment, string? orgId, Dictionary<string, string?>? settings = null)
    {
        var httpContext = new DefaultHttpContext();
        if (orgId is not null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CurrentTenantService.OrgIdClaim, orgId)], "test"));
        }

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        return new CurrentTenantService(accessor, new FakeEnvironment(environment), configuration);
    }

    [Fact]
    public void Uses_the_org_id_claim()
    {
        Assert.Equal("org_A", Create(Environments.Production, "org_A").TenantId);
    }

    [Fact]
    public void Returns_null_without_organization_in_production()
    {
        Assert.Null(Create(Environments.Production, orgId: null).TenantId);
    }

    [Fact]
    public void Uses_the_configured_fallback_without_organization_in_development()
    {
        var service = Create(Environments.Development, orgId: null, new() { ["Tenancy:DevelopmentFallbackTenant"] = "dev-tenant" });

        Assert.Equal("dev-tenant", service.TenantId);
    }

    [Fact]
    public async Task Explicit_tenant_overrides_the_claim_only_in_its_async_flow()
    {
        var service = Create(Environments.Production, "org_A");

        var inBackground = await Task.Run(() =>
        {
            service.SetTenant("org_B");
            return service.TenantId;
        });

        Assert.Equal("org_B", inBackground);
        Assert.Equal("org_A", service.TenantId);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
