using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Application.Common.Interfaces;
namespace AsistenteAyuntamiento.ApiService.Features.Tenants;

public class CurrentTenantService : AsistenteAyuntamiento.Application.Common.Interfaces.ICurrentTenantService
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
    
    private string? _tenantIdOverride;

    public string TenantId => _tenantIdOverride ?? _httpContextAccessor.HttpContext?.User.FindFirst("org_id")?.Value ?? "default";

    public void SetTenant(string tenantId)
    {
        _tenantIdOverride = tenantId;
    }

}
