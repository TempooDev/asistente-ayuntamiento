using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AsistenteAyuntamiento.Web.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddClientServices(builder.Configuration);

await builder.Build().RunAsync();
