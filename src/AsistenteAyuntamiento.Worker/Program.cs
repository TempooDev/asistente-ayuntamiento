using AsistenteAyuntamiento.Infrastructure.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Required by AppDbContext even if it's always null in a worker
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AsistenteAyuntamiento.Application.Common.Interfaces.ICurrentTenantService, AsistenteAyuntamiento.Worker.WorkerTenantService>();

// Configure Database
builder.AddNpgsqlDbContext<AppDbContext>(
    "asistente-ayuntamiento-db",
    configureDbContextOptions: options => options.UseNpgsql(npgsqlOptions => npgsqlOptions.UseVector()));

// Configure RabbitMQ
builder.AddRabbitMQClient("messaging");

// Configure S3 & Semantic Kernel (Infrastructure)
builder.AddInfrastructureServices();

var pipelineMode = builder.Configuration["WORKER_PIPELINE_MODE"] ?? "BASELINE";

if (pipelineMode.Equals("HIERARCHICAL", StringComparison.OrdinalIgnoreCase))
{
    // New Hierarchical Pipeline (Phase 2)
    builder.Services.AddScoped<AsistenteAyuntamiento.Worker.Services.FragmentEnrichmentService>();
    builder.Services.AddScoped<AsistenteAyuntamiento.Worker.Services.IngestionMetricsService>();
    builder.Services.AddScoped<AsistenteAyuntamiento.Worker.Services.BoeIngestionService>();
    builder.Services.AddScoped<AsistenteAyuntamiento.Worker.Services.BojaIngestionService>();
    
    // Register the hierarchical consumer
    builder.Services.AddHostedService<AsistenteAyuntamiento.Worker.Services.HierarchicalRabbitMqConsumerService>();
}
else
{
    // Default Baseline Pipeline
    builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
    builder.Services.AddHostedService<RabbitMqConsumerService>();
}

builder.Services.AddSingleton<AsistenteAyuntamiento.Application.Common.Interfaces.INotificationService, AsistenteAyuntamiento.Infrastructure.Features.Notifications.RabbitMqNotificationPublisher>();

var host = builder.Build();
host.Run();
