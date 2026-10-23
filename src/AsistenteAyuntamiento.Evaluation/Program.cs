using System.Text.Json;
using AsistenteAyuntamiento.Application;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Evaluation;
using AsistenteAyuntamiento.Infrastructure;
using AsistenteAyuntamiento.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

// Offline RAG evaluation (spec: rag-evaluation). Usage:
//   dotnet run --project src/AsistenteAyuntamiento.Evaluation -- [--pipelines Baseline,Hierarchical] [--limit N]
//       [--seed N] [--resume RUN_ID] [--dataset DIR] [--output DIR]
// It opens no network endpoint: it calls the pipelines in-process with the same services as the API.
var options = EvaluationArguments.Parse(args);
var repository = EvaluationArguments.FindRepository();
var datasetDirectory = options.Dataset ?? Path.Combine(repository, "eval", "dataset");
var runsDirectory = options.Output ?? Path.Combine(repository, "eval", "runs");

var builder = Host.CreateApplicationBuilder();

// One generation model for every pipeline, at temperature 0, whatever the host's chat settings are.
var chatProvider = builder.Configuration["Evaluation:ChatProvider"] ?? "ollama";
var chatModel = builder.Configuration["Evaluation:ChatModel"] ?? "qwen2.5:7b-instruct-q4_K_M";
const double temperature = 0;
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Ai:Chat:Provider"] = chatProvider,
    ["Ai:Chat:Model"] = chatModel,
    ["Ai:Chat:Temperature"] = "0",
    ["Ai:Ingestion:Provider"] = chatProvider,
    ["Ai:Ingestion:Model"] = chatModel,
});

// AddApplicationServices registers services this tool never resolves (chat sessions, Arena): skip the build-time check.
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true }));
builder.AddServiceDefaults();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ICurrentTenantService, EvaluationTenantService>();
builder.AddNpgsqlDbContext<AppDbContext>(
    "asistente-ayuntamiento-db",
    configureDbContextOptions: o => o.UseNpgsql(npgsql => npgsql.UseVector()));
builder.Services.AddApplicationServices(builder.Configuration);
builder.AddInfrastructureServices();
builder.Services.AddSingleton(sp => new EvaluationPreflight(
    sp.GetRequiredService<AsistenteAyuntamiento.Application.Common.VectorStore.IVectorStore>(),
    sp.GetRequiredService<Kernel>().GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()));
builder.Services.AddScoped<IndexingReporter>();
builder.Services.AddSingleton<EvaluationRunner>();

// Not started: the background processors of the API and Worker have nothing to do here.
using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

var dataset = EvaluationDataset.Load(datasetDirectory);
if (options.Limit is { } limit)
{
    dataset = dataset with { Questions = dataset.Questions.Take(limit).ToList() };
}

var problems = await host.Services.GetRequiredService<EvaluationPreflight>().CheckAsync(options.Pipelines, dataset.CorpusDocumentIds, cancellation.Token);
if (problems.Count > 0)
{
    foreach (var problem in problems)
    {
        logger.LogError("Pre-flight: {Problem}", problem);
    }
    return 1;
}

var runId = options.Resume ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var runDirectory = Path.Combine(runsDirectory, runId);
var runFile = Path.Combine(runDirectory, EvaluationRunner.RunFile);
RunInfo info;
if (options.Resume is not null && File.Exists(runFile))
{
    // A resumed run keeps its seed and models; mixing settings would make its answers incomparable.
    info = JsonSerializer.Deserialize<RunInfo>(File.ReadAllText(runFile), EvaluationJson.Options)!;
    if (info.ChatModel != chatModel || info.DatasetHash != dataset.DatasetHash)
    {
        logger.LogError("Run {RunId} used model {Model} and dataset {Hash}; it cannot be resumed with {CurrentModel} and {CurrentHash}",
            runId, info.ChatModel, info.DatasetHash, chatModel, dataset.DatasetHash);
        return 1;
    }
}
else
{
    info = new RunInfo(runId, DateTime.UtcNow, null, EvaluationArguments.GitCommit(repository), options.Seed ?? Random.Shared.Next(),
        chatProvider, chatModel, temperature, builder.Configuration["Ai:Embeddings:Model"] ?? "qwen3-embedding:0.6b",
        host.Services.GetRequiredService<QueryEmbeddingOptions>().Instruction, options.Pipelines, dataset.Questions.Count, dataset.DatasetHash, dataset.CorpusHash);
}

logger.LogInformation("Run {RunId}: {Questions} questions × {Pipelines} with {Model} (seed {Seed}) → {Directory}",
    runId, dataset.Questions.Count, string.Join(", ", info.Pipelines), chatModel, info.Seed, runDirectory);
await host.Services.GetRequiredService<EvaluationRunner>()
    .RunAsync(dataset, new EvaluationRunSettings(runDirectory, info.Pipelines, info.Seed, info), cancellation.Token);
logger.LogInformation("Run {RunId} finished. Score it with: cd eval/ragas && uv run python score.py ../runs/{RunId}", runId, runId);
return 0;

public partial class Program;
