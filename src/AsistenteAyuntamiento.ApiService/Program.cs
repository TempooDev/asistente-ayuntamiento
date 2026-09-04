using AsistenteAyuntamiento.Infrastructure;

using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Infrastructure.Features.Chat;
using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Application.Features.Ingestion;
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

builder.Services.AddSingleton<CurrentTenantService>();
builder.Services.AddSingleton<ICurrentTenantService>(sp => sp.GetRequiredService<CurrentTenantService>());
builder.Services.AddSingleton<IAiMetricsService, AiMetricsService>();
builder.Services.AddScoped<IChatSessionService, ChatSessionService>();
builder.Services.AddScoped<IAiChatService, AiChatService>();
builder.Services.AddSingleton<ChatMessageBuffer>();
builder.Services.AddSingleton<AsistenteAyuntamiento.ApiService.Features.Scraper.ScraperStateService>();
builder.Services.AddHostedService<ChatPersistenceWorker>();

builder.Services.AddDataProtection();
builder.Services.AddScoped<IAiConfigurationService, AiConfigurationService>();

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
builder.Services.AddHostedService<AsistenteAyuntamiento.ApiService.Features.Notifications.RabbitMqNotificationConsumer>();
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

// Register Semantic Kernel and S3 using the Infrastructure extension
builder.AddInfrastructureServices();
builder.AddRabbitMQClient("messaging");

// Registramos el IngestionService en el API solo para permitir peticiones de reprocesado manual,
// pero el consumidor automático en background (RabbitMqConsumerService) ahora se ejecuta exclusivamente en el Worker.
builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();

// Arena Services
builder.Services.AddScoped<AsistenteAyuntamiento.Application.Features.Retrieval.IQueryExpansionService, AsistenteAyuntamiento.Application.Features.Retrieval.QueryExpansionService>();
builder.Services.AddScoped<AsistenteAyuntamiento.Application.Features.Retrieval.IHybridRetrievalService, AsistenteAyuntamiento.Application.Features.Retrieval.HybridRetrievalService>();
builder.Services.AddScoped<AsistenteAyuntamiento.Application.Features.Generation.IClearLanguageGenerationService, AsistenteAyuntamiento.Application.Features.Generation.ClearLanguageGenerationService>();
builder.Services.AddScoped<AsistenteAyuntamiento.Application.Features.Arena.IArenaService, AsistenteAyuntamiento.Application.Features.Arena.ArenaService>();
builder.Services.AddScoped<AsistenteAyuntamiento.Application.Features.Metrics.IReadabilityService, AsistenteAyuntamiento.Application.Features.Metrics.ReadabilityService>();

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
            var bucketName = app.Configuration["Blob:BucketName"] ?? AsistenteAyuntamiento.Domain.Common.AppConstants.BlobStorage.DefaultBucketName;
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
AsistenteAyuntamiento.ApiService.Features.Arena.ArenaEndpoints.MapArenaEndpoints(app);
app.MapAiMetricsEndpoints();

app.MapDefaultEndpoints();

app.Run();
