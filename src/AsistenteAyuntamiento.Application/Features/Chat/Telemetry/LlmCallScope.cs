namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>Who an LLM call is made for and why, for the metrics labels.</summary>
/// <param name="TenantId">Tenant of the request, or <see cref="PlatformTenant"/> for platform-wide work (ingestion).</param>
/// <param name="Pipeline">Value of <c>app.pipeline</c> (<see cref="LlmPipelines"/>).</param>
/// <param name="Operation">Value of <c>app.llm.operation</c> (<see cref="LlmOperations"/>).</param>
public sealed record LlmCallScope(string TenantId, string Pipeline, string Operation = LlmOperations.Answer)
{
    /// <summary>Tenant label for work that belongs to no tenant (bulletins are shared).</summary>
    public const string PlatformTenant = "platform";
}
