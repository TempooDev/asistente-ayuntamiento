using AsistenteAyuntamiento.Application.Common.Interfaces;

namespace AsistenteAyuntamiento.ApiService.Features.Tenants;

/// <summary>
/// Resolves the current tenant (Auth0 Organization ID) from the <c>org_id</c> claim.
/// Returns <c>null</c> when there is no organization, except in Development, where
/// <c>Tenancy:DevelopmentFallbackTenant</c> (default <c>"default"</c>) is used.
/// Background work must call <see cref="SetTenant"/> explicitly before touching tenant-scoped data.
/// </summary>
public class CurrentTenantService(
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment,
    IConfiguration configuration) : ICurrentTenantService
{
    public const string OrgIdClaim = "org_id";

    private readonly AsyncLocal<string?> _tenantIdOverride = new();

    private readonly string? _developmentFallbackTenant = environment.IsDevelopment()
        ? configuration["Tenancy:DevelopmentFallbackTenant"] ?? "default"
        : null;

    public string? TenantId =>
        _tenantIdOverride.Value
        ?? httpContextAccessor.HttpContext?.User.FindFirst(OrgIdClaim)?.Value
        ?? _developmentFallbackTenant;

    public void SetTenant(string tenantId)
    {
        _tenantIdOverride.Value = tenantId;
    }
}
