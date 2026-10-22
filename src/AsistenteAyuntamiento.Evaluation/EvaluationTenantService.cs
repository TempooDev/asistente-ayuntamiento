using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>A tenant with no AI configuration of its own, so every pipeline answers with the platform model.</summary>
public sealed class EvaluationTenantService : ICurrentTenantService
{
    public string TenantId => EvaluationTurn.DefaultTenant;

    public void SetTenant(string tenantId)
    {
    }
}
