namespace AsistenteAyuntamiento.Application.Common.Interfaces;

public interface ICurrentTenantService
{
    string? TenantId { get; }
    void SetTenant(string tenantId);
}
