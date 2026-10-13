namespace AsistenteAyuntamiento.Worker;

/// <summary>
/// Fails fast at startup when a setting the Worker cannot ingest without is missing, instead of failing every message.
/// </summary>
public static class RequiredConfiguration
{
    private static readonly string[] AlwaysRequired =
    [
        "ConnectionStrings:asistente-ayuntamiento-db",
        "ConnectionStrings:messaging",
        "ConnectionStrings:qdrant",
        // Every message points at a blob; without these IBlobStorage is not registered.
        "Blob:Endpoint",
        "Blob:AccessKeyId",
        "Blob:SecretAccessKey",
    ];

    private static readonly string[] ProvidersRequiringKey = ["google", "openai", "openrouter"];

    public static IReadOnlyList<string> FindMissing(IConfiguration configuration)
    {
        var missing = AlwaysRequired.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToList();

        // Same defaults and fallbacks as the Semantic Kernel registration in Infrastructure.
        if (RequiresKey(configuration["Ai:Embeddings:Provider"])
            && string.IsNullOrWhiteSpace(configuration["Ai:Embeddings:ApiKey"]))
        {
            missing.Add("Ai:Embeddings:ApiKey");
        }

        var ingestionProvider = configuration["Ai:Ingestion:Provider"] ?? configuration["Ai:Chat:Provider"];
        var ingestionApiKey = configuration["Ai:Ingestion:ApiKey"] ?? configuration["Ai:Chat:ApiKey"];
        if (RequiresKey(ingestionProvider) && string.IsNullOrWhiteSpace(ingestionApiKey))
        {
            missing.Add("Ai:Ingestion:ApiKey (or Ai:Chat:ApiKey)");
        }

        return missing;
    }

    public static void Validate(IConfiguration configuration)
    {
        var missing = FindMissing(configuration);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required Worker configuration: {string.Join(", ", missing)}");
        }
    }

    private static bool RequiresKey(string? provider) =>
        provider is not null && ProvidersRequiringKey.Contains(provider.ToLowerInvariant());
}
