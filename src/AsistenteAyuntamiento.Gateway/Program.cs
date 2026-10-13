using AsistenteAyuntamiento.Gateway.Telemetry;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.AddServiceDefaults();
builder.AddFaroForwarding();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseFaroForwarding();

if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference("/docs", options =>
    {
        options.WithTitle("Asistente Ayuntamiento API (Gateway)");
        options.WithTheme(ScalarTheme.Mars);
    });
}

// Servir archivos estáticos de Angular (producción)
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapReverseProxy();

if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();

/// <summary>Entry point, public for the integration tests.</summary>
public partial class Program;
