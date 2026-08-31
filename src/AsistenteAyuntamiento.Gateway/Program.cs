using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add OpenApi to ensure it can reference documents if needed, though we just host Scalar
builder.Services.AddOpenApi();

builder.AddServiceDefaults();

// Add YARP reverse proxy and configure it
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    // Render Scalar at /docs and point it to the proxied OpenAPI JSON
    app.MapScalarApiReference("/docs", options =>
    {
        options.WithTitle("Asistente Ayuntamiento API (Gateway)");
        options.WithTheme(ScalarTheme.Mars);
    });
}

app.MapReverseProxy();

app.Run();
