using AsistenteAyuntamiento.Application.Common.Interfaces;

namespace AsistenteAyuntamiento.ApiService.Features.Tenants;

public class CurrentTenantService(IHttpContextAccessor httpContextAccessor) : ICurrentTenantService
{
    /// <summary>
    /// Gets the current TenantId (Auth0 Organization ID) from the JWT token.
    /// Returns "default" if not present (useful for local development or system admins).
    /// </summary>

    private readonly AsyncLocal<string?> _tenantIdOverride = new();

    public string TenantId => _tenantIdOverride.Value ?? httpContextAccessor.HttpContext?.User.FindFirst("org_id")?.Value ?? "default";

    public void SetTenant(string tenantId)
    {
        _tenantIdOverride.Value = tenantId;
    }

}
