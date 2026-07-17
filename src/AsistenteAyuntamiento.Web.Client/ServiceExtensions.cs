
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
        services.AddScoped<AppTokenProvider>();
        services.AddScoped<ChatSignalRService>();
        
        services.AddHttpClient<WeatherApiClient>((sp, client) => {
            var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            client.BaseAddress = new Uri(navManager.BaseUri);
        });
        
        services.AddHttpClient<UserApiClient>((sp, client) => {
            var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            client.BaseAddress = new Uri(navManager.BaseUri);
        });

        // Register SignalR HubConnection as Transient so each component gets its own connection
        services.AddTransient<HubConnection>(sp =>
        {
            var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            // Hub url is relative to the base URL
            var hubUrl = navManager.ToAbsoluteUri("/hubs/chat");
            var tokenProvider = sp.GetRequiredService<AppTokenProvider>();

            return new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(tokenProvider.AccessToken);
                })
                .WithAutomaticReconnect()
                .Build();
        });

        return services;
    }
}
