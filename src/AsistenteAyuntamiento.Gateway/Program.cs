using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapDefaultEndpoints();

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

app.Run();
