using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using AsistenteAyuntamiento.Application.Features.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Infrastructure.Ai;

/// <summary>
/// Builds chat-completion services over pooled <see cref="IHttpClientFactory"/> clients
/// (TLS validated unless the Development-only <c>Ai:Http:AllowInsecureTls</c> flag is set).
/// </summary>
public sealed class ChatModelFactory(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IChatModelFactory
{
    public const string HttpClientName = "llm";

    private const string OpenRouterEndpoint = "https://openrouter.ai/api/v1";
    private const string DefaultFallbackModel = "minimax/minimax-m3:free";

    private static readonly string[] ProvidersRequiringKey = ["google", "openai", "openrouter"];

    public IChatCompletionService Create(AiConfigurationDto config, string? apiKey)
    {
        var provider = config.Provider?.ToLowerInvariant() ?? "ollama";
        var modelId = config.Model ?? string.Empty;

        if (ProvidersRequiringKey.Contains(provider) && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new LlmNotConfiguredException($"No API key configured for provider '{provider}'.");
        }

        var kernelBuilder = Kernel.CreateBuilder();

        switch (provider)
        {
            case "google":
                kernelBuilder.AddGoogleAIGeminiChatCompletion(modelId, apiKey!, httpClient: CreateHttpClient());
                break;

            case "openai":
            case "openrouter":
                var endpoint = string.IsNullOrEmpty(config.EndpointUrl) && provider == "openrouter"
                    ? OpenRouterEndpoint
                    : config.EndpointUrl;
                kernelBuilder.AddOpenAIChatCompletion(modelId, apiKey!, httpClient: CreateHttpClient(endpoint));
                break;

            default:
                kernelBuilder.AddOllamaChatCompletion(modelId, CreateHttpClient(OllamaEndpoint()));
                break;
        }

        return kernelBuilder.Build().GetRequiredService<IChatCompletionService>();
    }

    public FallbackChatModel? CreateFallback()
    {
        var section = configuration.GetSection("Ai:Fallback");
        var apiKey = section["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var modelId = section["ModelId"] ?? DefaultFallbackModel;
        var endpoint = section["Endpoint"] ?? OpenRouterEndpoint;

        // Platform credentials only: the tenant's key never reaches the fallback provider.
        var service = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId, apiKey, httpClient: CreateHttpClient(endpoint))
            .Build()
            .GetRequiredService<IChatCompletionService>();

        return new FallbackChatModel(modelId, service, section["Provider"] ?? "openrouter");
    }

    private HttpClient CreateHttpClient(string? baseAddress = null)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        if (!string.IsNullOrEmpty(baseAddress))
        {
            client.BaseAddress = new Uri(baseAddress);
        }
        return client;
    }

    private string OllamaEndpoint()
    {
        var connectionString = configuration.GetConnectionString("ollama") ?? "http://localhost:11434";
        return connectionString.StartsWith("Endpoint=", StringComparison.Ordinal)
            ? connectionString.Split(';').First(p => p.StartsWith("Endpoint=", StringComparison.Ordinal))["Endpoint=".Length..]
            : connectionString;
    }
}
