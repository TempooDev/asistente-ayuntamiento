using AsistenteAyuntamiento.Application.Common.Interfaces;

namespace AsistenteAyuntamiento.Worker;

public class WorkerTenantService : ICurrentTenantService
{
    public string TenantId => "system_worker";
    
    public void SetTenant(string tenantId)
    {
        // No-op for worker unless we need to impersonate
    }
}
