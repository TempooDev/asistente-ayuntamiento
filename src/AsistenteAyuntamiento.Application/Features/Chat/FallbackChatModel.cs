using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>The platform's fallback model (<c>Ai:Fallback:*</c>), used when a tenant's quota is exhausted.</summary>
public sealed record FallbackChatModel(string ModelId, IChatCompletionService Service, string Provider = "openrouter");
