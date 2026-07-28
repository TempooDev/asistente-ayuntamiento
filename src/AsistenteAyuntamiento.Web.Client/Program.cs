using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AsistenteAyuntamiento.Web.Client;
using Microsoft.Extensions.Options;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddClientServices(builder.Configuration);

// Set BaseAddress for typed HttpClients — in WASM, API calls go through the gateway at the host origin.
builder.Services.AddHttpClient<AsistenteAyuntamiento.Web.Client.WeatherApiClient>((sp, client) =>
{
    var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    client.BaseAddress = new Uri(navManager.BaseUri);
});
builder.Services.AddHttpClient<AsistenteAyuntamiento.Web.Client.UserApiClient>((sp, client) =>
{
    var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    client.BaseAddress = new Uri(navManager.BaseUri);
});
builder.Services.AddHttpClient<AsistenteAyuntamiento.Web.Client.AiConfigApiClient>((sp, client) =>
{
    var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    client.BaseAddress = new Uri(navManager.BaseUri);
});

// SignalR hub URL — in WASM, connect via the browser origin (gateway routes /hubs/* → apiservice)
builder.Services.AddTransient<IConfigureOptions<ChatHubOptions>>(sp =>
{
    var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    var hubUrl = navManager.ToAbsoluteUri("/hubs/chat").ToString();
    return new ConfigureNamedOptions<ChatHubOptions>(Options.DefaultName, o => o.HubUrl = hubUrl);
});

await builder.Build().RunAsync();
