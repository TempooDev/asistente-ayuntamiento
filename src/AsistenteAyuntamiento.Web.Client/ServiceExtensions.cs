using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.Web.Client;

public static class ServiceExtensions
{
    /// <summary>
    /// Registers services that must be available in both SSR (server) and WASM (client) contexts.
    /// Call this from both the server Program.cs and the WASM Program.cs.
    /// </summary>
    public static IServiceCollection AddClientServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient<WeatherApiClient>(client =>
        {
            // In WASM the base address is the app's origin.
            // In SSR it will be overridden by the server's HttpClient factory.
            var apiBase = configuration["ApiServiceBaseUrl"] ?? "https+http://apiservice";
            client.BaseAddress = new Uri(apiBase);
        });

        return services;
    }
}
