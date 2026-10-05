using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Infrastructure.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure;
using Microsoft.EntityFrameworkCore;
using AsistenteAyuntamiento.Application.Common;
using AsistenteAyuntamiento.Worker.Services;

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

builder.Services.AddSingleton<IChunkingStrategy>(sp => 
{
    var config = sp.GetRequiredService<IConfiguration>();
    var maxTokens = config.GetValue<int>("Ai:Embeddings:ChunkMaxTokens", 400);
    var overlapTokens = config.GetValue<int>("Ai:Embeddings:ChunkOverlapTokens", 50);
    return new FixedOverlapChunkingStrategy(maxTokens, overlapTokens);
});

var pipelineModeStr = builder.Configuration["WORKER_PIPELINE_MODE"] ?? "BASELINE";
var pipelineMode = Enum.TryParse<PipelineType>(pipelineModeStr, true, out var p) ? p : PipelineType.Baseline;

if (pipelineMode == PipelineType.Hierarchical)
{
    // New Hierarchical Pipeline (Phase 2)
    builder.Services.AddScoped<IFragmentEnrichmentService, FragmentEnrichmentService>();
    builder.Services.AddScoped<IIngestionMetricsService, IngestionMetricsService>();
    builder.Services.AddKeyedScoped<IHierarchicalIngestionProcessor, BoeIngestionService>(BulletinType.BOE.ToString());
    builder.Services.AddKeyedScoped<IHierarchicalIngestionProcessor, BojaIngestionService>(BulletinType.BOJA.ToString());

    // Register the hierarchical consumer
    builder.Services.AddHostedService<HierarchicalRabbitMqConsumerService>();
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

