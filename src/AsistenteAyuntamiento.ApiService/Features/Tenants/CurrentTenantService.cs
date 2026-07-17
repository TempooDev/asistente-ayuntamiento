namespace AsistenteAyuntamiento.ApiService.Features.Tenants;

public class CurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current TenantId (Auth0 Organization ID) from the JWT token.
    /// Returns "default" if not present (useful for local development or system admins).
    /// </summary>
    public string TenantId => _httpContextAccessor.HttpContext?.User.FindFirst("org_id")?.Value ?? "default";
}
