using AsistenteAyuntamiento.ApiService.Features.AiConfig;
using AsistenteAyuntamiento.ApiService.Features.Arena;
using AsistenteAyuntamiento.ApiService.Features.Chat;
using AsistenteAyuntamiento.ApiService.Features.Config;
using AsistenteAyuntamiento.ApiService.Features.Ingestion;
using AsistenteAyuntamiento.ApiService.Features.Notifications;
using AsistenteAyuntamiento.ApiService.Features.Scraper;
using AsistenteAyuntamiento.ApiService.Features.Tenants;
using AsistenteAyuntamiento.ApiService.Features.Users;
using AsistenteAyuntamiento.ApiService.Grpc;
using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.Application;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Application.Features.Metrics;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Application.Features.Scraper;
using AsistenteAyuntamiento.Application.Features.Users;
using AsistenteAyuntamiento.Infrastructure;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Features.Chat;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Polly;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

RequiredConfiguration.Validate(builder.Configuration, builder.Environment);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<ICurrentTenantService, CurrentTenantService>();


builder.Services.AddHostedService<ChatPersistenceWorker>();

builder.Services.AddDataProtection();

builder.AddNpgsqlDbContext<AppDbContext>(
    "asistente-ayuntamiento-db",
    configureDbContextOptions: options => options
        .UseNpgsql(npgsqlOptions => npgsqlOptions.UseVector()));

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

        // SignalR sends the access token in the query string for WebSockets/SSE (every hub under /hubs)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAppAuthorization();
// The tenant comes from the connection user, so hub calls work with every SignalR transport.
builder.Services.AddSignalR(options => options.AddFilter<TenantHubFilter>());
builder.Services.AddSingleton<AsistenteAyuntamiento.Application.Common.Interfaces.INotificationService, SignalRNotificationService>();
builder.Services.AddHostedService<RabbitMqNotificationConsumer>();
builder.Services.AddGrpc();
builder.Services.AddSingleton<InternalApiKeyInterceptor>();
builder.Services.AddGrpc().AddServiceOptions<FilterConfigServiceImpl>(options =>
    options.Interceptors.Add<InternalApiKeyInterceptor>());
builder.Services.AddSingleton<InternalApiKeyClientInterceptor>();
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental.
builder.Services.AddGrpcClient<AsistenteAyuntamiento.ApiService.Protos.ScraperCommandService.ScraperCommandServiceClient>(o =>
{
    var goScraperUrl = builder.Configuration["GoScraper:GrpcUrl"] ?? "http://localhost:50051";
    o.Address = new Uri(goScraperUrl);
})
.AddInterceptor<InternalApiKeyClientInterceptor>()
// ForceScrape runs a whole scrape (minutes) and is not idempotent, so the ServiceDefaults handler (10 s per attempt,
// 30 s total, retries) is removed: only a 30-minute timeout applies and a failed call is never sent again.
.RemoveAllResilienceHandlers()
.AddResilienceHandler("scraper-command", pipeline => pipeline.AddTimeout(TimeSpan.FromMinutes(30)));
#pragma warning restore EXTEXP0001

// Register Semantic Kernel and S3 using the Infrastructure extension
builder.Services.AddApplicationServices(builder.Configuration);
builder.AddInfrastructureServices();
builder.AddRabbitMQClient("messaging");



// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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
    if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AsistenteAyuntamiento.Infrastructure.Data.AppDbContext>();
        dbContext.Database.Migrate();
    }

    var blobStorage = scope.ServiceProvider.GetService<IBlobStorage>();
    if (blobStorage != null)
    {
        blobStorage.EnsureContainerAsync().GetAwaiter().GetResult();
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantRequiredMiddleware>();

app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapGrpcService<FilterConfigServiceImpl>();

app.MapUserEndpoints();
app.MapAiConfigEndpoints();
app.MapConfigEndpoints();
app.MapIngestionEndpoints();
app.MapScraperFilterEndpoints();
app.MapArenaEndpoints();
app.MapAiMetricsEndpoints();

app.MapDefaultEndpoints();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
