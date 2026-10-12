namespace AsistenteAyuntamiento.ApiService.Security;

/// <summary>
/// Fails fast at startup when a setting the API cannot run safely without is missing.
/// </summary>
public static class RequiredConfiguration
{
    private static readonly string[] AlwaysRequired =
    [
        "Auth0:Domain",
        "Auth0:Audience",
    ];

    private static readonly string[] ProductionRequired =
    [
        "Auth0:CustomClaimsNamespace",
        "Blob:Endpoint",
        "Blob:AccessKeyId",
        "Blob:SecretAccessKey",
        InternalApiKeyInterceptor.ConfigurationKey,
    ];

    public static IReadOnlyList<string> FindMissing(IConfiguration configuration, IHostEnvironment environment)
    {
        var keys = environment.IsProduction() ? AlwaysRequired.Concat(ProductionRequired) : AlwaysRequired;
        return keys.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToList();
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        var missing = FindMissing(configuration, environment);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required configuration for environment '{environment.EnvironmentName}': {string.Join(", ", missing)}");
        }
    }
}
