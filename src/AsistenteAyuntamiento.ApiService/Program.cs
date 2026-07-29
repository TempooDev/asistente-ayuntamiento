using AsistenteAyuntamiento.ApiService.Features.Chat;
using AsistenteAyuntamiento.ApiService.Features.Tenants;
using AsistenteAyuntamiento.ApiService.Features.AiConfig;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<CurrentTenantService>();
builder.Services.AddSingleton<AiMetricsService>();
builder.Services.AddScoped<ChatSessionService>();
builder.Services.AddScoped<AiChatService>();
builder.Services.AddSingleton<ChatMessageBuffer>();
builder.Services.AddHostedService<ChatPersistenceWorker>();

builder.Services.AddDataProtection();
builder.Services.AddScoped<AiConfigurationService>();

builder.AddNpgsqlDbContext<AsistenteAyuntamiento.ApiService.Infrastructure.Data.AppDbContext>(
    "asistente-ayuntamiento-db",
    configureDbContextOptions: options => options.UseNpgsql(npgsqlOptions => npgsqlOptions.UseVector()));

var auth0Domain = builder.Configuration["Auth0:Domain"];
var auth0Audience = builder.Configuration["Auth0:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://{auth0Domain}/";
        options.Audience = auth0Audience;
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            RoleClaimType = "https://asistente.ayuntamiento.com/roles"
        };

        // SignalR sends the access token in the query string for WebSockets
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

// Register Semantic Kernel with Ollama
#pragma warning disable SKEXP0070 // Ollama connector is experimental
var ollamaConnString = builder.Configuration.GetConnectionString("ollama") ?? "http://localhost:11434";
var ollamaEndpoint = ollamaConnString.StartsWith("Endpoint=") 
    ? ollamaConnString.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length) 
    : ollamaConnString;
builder.Services.AddKernel()
    .AddOllamaChatCompletion("llama3.2", new Uri(ollamaEndpoint))
#pragma warning disable SKEXP0001
    .AddOllamaTextEmbeddingGeneration("nomic-embed-text", new Uri(ollamaEndpoint));
#pragma warning restore SKEXP0001
#pragma warning restore SKEXP0070

builder.AddRabbitMQClient("messaging");
var blobEndpoint = builder.Configuration["Blob:Endpoint"];
if (!string.IsNullOrEmpty(blobEndpoint))
{
    var accessKeyId = builder.Configuration["Blob:AccessKeyId"] ?? "admin";
    var secretAccessKey = builder.Configuration["Blob:SecretAccessKey"] ?? "password123";
    var s3Config = new Amazon.S3.AmazonS3Config { ServiceURL = blobEndpoint, ForcePathStyle = true };
    var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKeyId, secretAccessKey);
    builder.Services.AddSingleton<Amazon.S3.IAmazonS3>(new Amazon.S3.AmazonS3Client(credentials, s3Config));
}
builder.Services.AddScoped<AsistenteAyuntamiento.ApiService.Features.Ingestion.DocumentIngestionService>();
builder.Services.AddHostedService<AsistenteAyuntamiento.ApiService.Features.Ingestion.RabbitMqConsumerService>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register AI metrics OpenTelemetry instruments
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(AiMetricsService.MeterName))
    .WithTracing(tracing => tracing.AddSource(AiMetricsService.MeterName));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Apply database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AsistenteAyuntamiento.ApiService.Infrastructure.Data.AppDbContext>();
    dbContext.Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapHub<AsistenteAyuntamiento.ApiService.Features.Chat.ChatHub>("/hubs/chat");

AsistenteAyuntamiento.ApiService.Features.Users.UserEndpoints.MapUserEndpoints(app);
app.MapAiMetricsEndpoints();
AsistenteAyuntamiento.ApiService.Features.AiConfig.AiConfigEndpoints.MapAiConfigEndpoints(app);
AsistenteAyuntamiento.ApiService.Features.Ingestion.IngestionEndpoints.MapIngestionEndpoints(app);

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
