using AsistenteAyuntamiento.Application;
using AsistenteAyuntamiento.Application.Common;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Infrastructure;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Features.Ingestion;
using AsistenteAyuntamiento.Worker;
using Microsoft.EntityFrameworkCore;

// Container healthcheck: the runtime image ships no curl or wget, so the Worker probes itself.
if (args is [HealthProbe.Argument, var probeUrl])
{
    return await HealthProbe.RunAsync(probeUrl);
}

var builder = WebApplication.CreateBuilder(args);

RequiredConfiguration.Validate(builder.Configuration);

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

// AddApplicationServices also registers the chat services (AiConfigurationService needs Data Protection). The
// Worker never resolves them, but Development validates every registration when the app is built.
builder.Services.AddDataProtection();

// Application services, then their ports (S3, Qdrant, Semantic Kernel, RabbitMQ)
builder.Services.AddApplicationServices(builder.Configuration);
builder.AddInfrastructureServices();

// One consumer per Worker: BASELINE (default, also for unknown values), HIERARCHICAL, RECURSIVE or SEMANTIC.
switch (WorkerPipelineMode.Parse(builder.Configuration["WORKER_PIPELINE_MODE"]))
{
    case PipelineType.Hierarchical:
        builder.AddIngestionConsumer<HierarchicalIngestionConsumer>();
        break;
    case PipelineType.Recursive:
        builder.AddIngestionConsumer<RecursiveIngestionConsumer>();
        break;
    case PipelineType.Semantic:
        builder.AddIngestionConsumer<SemanticIngestionConsumer>();
        break;
    default:
        builder.AddIngestionConsumer<BaselineIngestionConsumer>();
        break;
}

builder.Services.AddSingleton<AsistenteAyuntamiento.Application.Common.Interfaces.INotificationService, AsistenteAyuntamiento.Infrastructure.Features.Notifications.RabbitMqNotificationPublisher>();

var app = builder.Build();

// The Worker has no public ingress (no published port, no gateway route), so its health endpoints are served in
// every environment for the compose healthcheck and the Aspire dashboard.
app.MapHealthEndpoints();

app.Run();
return 0;

// Exposed for WebApplicationFactory-based tests.
public partial class Program;

