using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
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
        var apiBase = configuration["ApiServiceBaseUrl"] ?? "https+http://apiservice";

        services.AddHttpClient<WeatherApiClient>(client =>
        {
            // In WASM the base address is the app's origin.
            // In SSR it will be overridden by the server's HttpClient factory.
            client.BaseAddress = new Uri(apiBase);
        });

        // Register SignalR HubConnection as Transient so each component gets its own connection
        services.AddTransient<HubConnection>(sp =>
        {
            var hubUrl = new Uri(new Uri(apiBase), "/chathub");
            var tokenProvider = sp.GetRequiredService<IAccessTokenProvider>();

            return new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        var result = await tokenProvider.RequestAccessToken();
                        if (result.TryGetToken(out var token))
                        {
                            return token.Value;
                        }
                        return null;
                    };
                })
                .WithAutomaticReconnect()
                .Build();
        });

        return services;
    }
}
