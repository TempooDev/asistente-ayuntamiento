namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>Who an LLM call is made for, for the metrics labels.</summary>
public sealed record LlmCallScope(string TenantId, string Pipeline);
