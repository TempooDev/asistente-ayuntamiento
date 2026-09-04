namespace AsistenteAyuntamiento.ApiService.Features.Tenants;

public class CurrentTenantService(IHttpContextAccessor httpContextAccessor) : AsistenteAyuntamiento.Application.Common.Interfaces.ICurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    /// <summary>
    /// Gets the current TenantId (Auth0 Organization ID) from the JWT token.
    /// Returns "default" if not present (useful for local development or system admins).
    /// </summary>

    private readonly AsyncLocal<string?> _tenantIdOverride = new();

    public string TenantId => _tenantIdOverride.Value ?? _httpContextAccessor.HttpContext?.User.FindFirst("org_id")?.Value ?? "default";

    public void SetTenant(string tenantId)
    {
        _tenantIdOverride.Value = tenantId;
    }

}
