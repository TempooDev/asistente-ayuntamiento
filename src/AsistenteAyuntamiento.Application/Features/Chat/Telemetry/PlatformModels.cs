using Microsoft.Extensions.Configuration;

namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>Provider and model of the platform kernels, as registered by Infrastructure.</summary>
public static class PlatformModels
{
    /// <summary>The default kernel: <c>Ai:Chat:*</c>.</summary>
    public static (string Provider, string Model) Chat(IConfiguration configuration) =>
        (configuration["Ai:Chat:Provider"] ?? "ollama", configuration["Ai:Chat:Model"] ?? "llama3.2");

    /// <summary>The <c>IngestionKernel</c>: <c>Ai:Ingestion:*</c>, defaulting to the chat model.</summary>
    public static (string Provider, string Model) Ingestion(IConfiguration configuration)
    {
        var (provider, model) = Chat(configuration);
        return (configuration["Ai:Ingestion:Provider"] ?? provider, configuration["Ai:Ingestion:Model"] ?? model);
    }
}
