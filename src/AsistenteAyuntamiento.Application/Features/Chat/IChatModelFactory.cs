using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Builds chat-completion clients (port; implemented in Infrastructure with pooled HTTP clients).
/// </summary>
public interface IChatModelFactory
{
    /// <summary>
    /// Builds the chat service for a tenant's AI configuration. The tenant's <paramref name="apiKey"/>
    /// is only ever sent to that configuration's provider.
    /// </summary>
    /// <exception cref="LlmNotConfiguredException">The provider needs an API key and none is configured.</exception>
    IChatCompletionService Create(AiConfigurationDto config, string? apiKey);

    /// <summary>
    /// Platform fallback model with its own credentials (<c>Ai:Fallback:*</c>), or <c>null</c> when not configured.
    /// </summary>
    FallbackChatModel? CreateFallback();
}
