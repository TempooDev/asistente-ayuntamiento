using AsistenteAyuntamiento.Infrastructure;
using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.ApiService.Features.Chat;
using AsistenteAyuntamiento.ApiService.Features.Tenants;
using AsistenteAyuntamiento.ApiService.Features.AiConfig;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using AsistenteAyuntamiento.ApiService.Features.Users;
using AsistenteAyuntamiento.ApiService.Features.Ingestion;
using Scalar.AspNetCore;

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
builder.Services.AddSingleton<AsistenteAyuntamiento.ApiService.Features.Scraper.ScraperStateService>();
builder.Services.AddHostedService<ChatPersistenceWorker>();

builder.Services.AddDataProtection();
builder.Services.AddScoped<AiConfigurationService>();

builder.AddNpgsqlDbContext<AppDbContext>(
    "asistente-ayuntamiento-db",
    configureDbContextOptions: options => options
        .UseNpgsql(npgsqlOptions => npgsqlOptions.UseVector())
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

var auth0Domain = builder.Configuration["Auth0:Domain"];
var auth0Audience = builder.Configuration["Auth0:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://{auth0Domain}/";
        options.Audience = auth0Audience;
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            RoleClaimType = $"{builder.Configuration["Auth0:CustomClaimsNamespace"]}/roles"
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
builder.Services.AddSingleton<AsistenteAyuntamiento.Application.Common.Interfaces.INotificationService, AsistenteAyuntamiento.ApiService.Features.Notifications.SignalRNotificationService>();
builder.Services.AddGrpc();
builder.Services.AddGrpcClient<AsistenteAyuntamiento.ApiService.Protos.ScraperCommandService.ScraperCommandServiceClient>(o =>
{
    var goScraperUrl = builder.Configuration["GoScraper:GrpcUrl"] ?? "http://localhost:50051";
    o.Address = new Uri(goScraperUrl);
})
.AddStandardResilienceHandler(options => 
{
    options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(30);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(30);
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(60);
});

// Register Semantic Kernel with configurable provider
#pragma warning disable SKEXP0070 // Experimental connectors warning
var ollamaConnString = builder.Configuration.GetConnectionString("ollama") ?? "http://localhost:11434";
var ollamaEndpoint = ollamaConnString.StartsWith("Endpoint=")
    ? ollamaConnString.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length)
    : ollamaConnString;

var chatProvider = builder.Configuration["Ai:Chat:Provider"] ?? "ollama";
var chatModel = builder.Configuration["Ai:Chat:Model"] ?? "llama3.2";
var chatApiKey = builder.Configuration["Ai:Chat:ApiKey"] ?? "";

var kernelBuilder = builder.Services.AddKernel();

if (chatProvider.Equals("google", StringComparison.OrdinalIgnoreCase))
{
    var handler = new SocketsHttpHandler { SslOptions = new System.Net.Security.SslClientAuthenticationOptions { CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck } };
    if (builder.Environment.IsDevelopment()) handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true;
    kernelBuilder.AddGoogleAIGeminiChatCompletion(chatModel, chatApiKey, httpClient: new HttpClient(handler));
}
else if (chatProvider.Equals("openai", StringComparison.OrdinalIgnoreCase))
{
    var chatEndpointUrl = builder.Configuration["Ai:Chat:EndpointUrl"];
    if (!string.IsNullOrEmpty(chatEndpointUrl))
    {
        var httpClient = new HttpClient { BaseAddress = new Uri(chatEndpointUrl) };
        kernelBuilder.AddOpenAIChatCompletion(chatModel, chatApiKey, httpClient: httpClient);
    }
    else
    {
        kernelBuilder.AddOpenAIChatCompletion(chatModel, chatApiKey);
    }
}
else
{
    var ollamaUriStr = ollamaEndpoint.TrimEnd('/');
    kernelBuilder.AddOllamaChatCompletion(chatModel, new Uri(ollamaUriStr));
}

var aiEmbeddingsConfig = builder.Configuration.GetSection("Ai:Embeddings");
var embProvider = aiEmbeddingsConfig["Provider"] ?? "ollama";
var embModel = aiEmbeddingsConfig["Model"] ?? "nomic-embed-text";
var embEndpoint = aiEmbeddingsConfig["EndpointUrl"] ?? ollamaEndpoint;
var embApiKey = aiEmbeddingsConfig["ApiKey"] ?? "";

if (embProvider.Equals("google", StringComparison.OrdinalIgnoreCase))
{
    var handler = new SocketsHttpHandler { SslOptions = new System.Net.Security.SslClientAuthenticationOptions { CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck } };
    if (builder.Environment.IsDevelopment()) handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true;
    kernelBuilder.AddGoogleAIEmbeddingGenerator(embModel, embApiKey, httpClient: new HttpClient(handler));
}
else if (embProvider.Equals("openai", StringComparison.OrdinalIgnoreCase))
{
#pragma warning disable SKEXP0010
    if (!string.IsNullOrEmpty(embEndpoint))
    {
        var httpClient = new HttpClient { BaseAddress = new Uri(embEndpoint) };
        kernelBuilder.AddOpenAIEmbeddingGenerator(embModel, embApiKey, httpClient: httpClient);
    }
    else
    {
        kernelBuilder.AddOpenAIEmbeddingGenerator(embModel, embApiKey);
    }
#pragma warning restore SKEXP0010
}
else
{
    var embUriStr = embEndpoint.StartsWith("Endpoint=") ? embEndpoint.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length) : embEndpoint;
    embUriStr = embUriStr.TrimEnd('/');
#pragma warning disable SKEXP0001
    kernelBuilder.AddOllamaEmbeddingGenerator(embModel, new Uri(embUriStr));
#pragma warning restore SKEXP0001
}
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
// Registramos el IngestionService en el API solo para permitir peticiones de reprocesado manual,
// pero el consumidor automático en background (RabbitMqConsumerService) ahora se ejecuta exclusivamente en el Worker.
builder.Services.AddScoped<DocumentIngestionService>();

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
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Asistente Ayuntamiento API");
        options.WithTheme(ScalarTheme.Mars); // Opcional, dale un poco de color
        options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

// Apply database migrations and ensure S3 bucket on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    dbContext.Database.Migrate();

    var s3Client = scope.ServiceProvider.GetService<Amazon.S3.IAmazonS3>();
    if (s3Client != null)
    {
        try
        {
            var bucketName = app.Configuration["Blob:BucketName"] ?? "boletines";
            s3Client.PutBucketAsync(new Amazon.S3.Model.PutBucketRequest
            {
                BucketName = bucketName,
                UseClientRegion = true
            }).GetAwaiter().GetResult();
        }
        catch (Amazon.S3.AmazonS3Exception e) when (e.ErrorCode == "BucketAlreadyOwnedByYou" || e.ErrorCode == "BucketAlreadyExists")
        {
            // Bucket already exists, all good
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Could not automatically create S3 bucket. It might already exist or require manual creation.");
        }
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<AsistenteAyuntamiento.ApiService.Features.Notifications.NotificationHub>("/hubs/notifications");

app.MapGrpcService<AsistenteAyuntamiento.ApiService.Features.Scraper.FilterConfigServiceImpl>();

UserEndpoints.MapUserEndpoints(app);
AiConfigEndpoints.MapAiConfigEndpoints(app);
AsistenteAyuntamiento.ApiService.Features.Config.ConfigEndpoints.MapConfigEndpoints(app);
IngestionEndpoints.MapIngestionEndpoints(app);
AsistenteAyuntamiento.ApiService.Features.Scraper.ScraperFilterEndpoints.MapScraperFilterEndpoints(app);
app.MapAiMetricsEndpoints();

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
