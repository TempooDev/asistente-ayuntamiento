
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.Web.Client;

public static class ServiceExtensions
{
    /// <summary>
    /// Registers services that must be available in both SSR (server) and WASM (client) contexts.
    /// Call this from both the server Program.cs and the WASM Program.cs.
    /// NOTE: BaseAddress for typed HttpClients is NOT set here — each host's Program.cs
    /// must configure the correct BaseAddress (server → http://apiservice, WASM → host origin).
    /// </summary>
    public static IServiceCollection AddClientServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<AppTokenProvider>();
        services.AddScoped<ChatSignalRService>();
        
        services.AddHttpClient<WeatherApiClient>();
        
        services.AddHttpClient<UserApiClient>();

        services.AddHttpClient<AiConfigApiClient>();
        services.AddHttpClient<IngestionApiClient>();

        return services;
    }
}

