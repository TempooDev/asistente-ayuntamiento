var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add YARP reverse proxy and configure it to use Aspire Service Discovery
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapReverseProxy();

app.Run();
