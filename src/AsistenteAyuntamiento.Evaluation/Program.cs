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
//       [--seed N] [--resume RUN_ID] [--dataset DIR] [--output DIR] [--score]   (--score: then score.py, report.py and import)
//   dotnet run --project src/AsistenteAyuntamiento.Evaluation -- --import RUN_ID|latest    (after score.py and report.py;
//   also the Aspire resource "evaluation-import")
//   EVAL_LATENCY_DB=... dotnet run --project src/AsistenteAyuntamiento.Evaluation -- --latency-report [--from 2026-11-01] [--to 2026-12-01]
// It opens no network endpoint: it calls the pipelines in-process with the same services as the API.
var options = EvaluationArguments.Parse(args);
var repository = EvaluationArguments.FindRepository();
var datasetDirectory = options.Dataset ?? Path.Combine(repository, "eval", "dataset");
var runsDirectory = options.Output ?? Path.Combine(repository, "eval", "runs");

if (options.LatencyReport)
{
    // Read-only aggregates for chapter 8 (dates in Spanish local time); the connection comes only from EVAL_LATENCY_DB (a read-only role, see eval/README.md).
    var connection = Environment.GetEnvironmentVariable(OperationalLatencyReport.ConnectionVariable);
    if (string.IsNullOrWhiteSpace(connection))
    {
        Console.Error.WriteLine($"Set {OperationalLatencyReport.ConnectionVariable} to a read-only connection string of the database.");
        return 1;
    }

    var to = options.To ?? (options.From is { } start ? start.AddDays(30) : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, OperationalLatencyReport.PeriodTimeZone)).AddDays(1));
    var from = options.From ?? to.AddDays(-30);
    if (from >= to)
    {
        Console.Error.WriteLine($"--from ({from:yyyy-MM-dd}) must be before --to ({to:yyyy-MM-dd}); --to is exclusive.");
        return 1;
    }
    var reportDirectory = options.Output ?? Path.Combine(repository, "eval", "latency", $"{from:yyyy-MM-dd}_{to:yyyy-MM-dd}");
    foreach (var file in await new OperationalLatencyReport(connection).WriteAsync(from, to, reportDirectory))
    {
        Console.WriteLine(file);
    }
    return 0;
}

var builder = Host.CreateApplicationBuilder();

// Outside Aspire the connection strings come from user secrets, in any environment:
// dotnet user-secrets set "ConnectionStrings:asistente-ayuntamiento-db" "Host=localhost;..." --project src/AsistenteAyuntamiento.Evaluation
builder.Configuration.AddUserSecrets<Program>(optional: true);

if (options.Import is { } importRunId)
{
    // Only the database: importing needs no models, vectors or messaging.
    builder.Services.AddSingleton<ICurrentTenantService, EvaluationTenantService>();
    builder.AddNpgsqlDbContext<AppDbContext>("asistente-ayuntamiento-db", configureDbContextOptions: o => o.UseNpgsql(npgsql => npgsql.UseVector()));
    builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
    builder.Services.AddScoped<EvaluationImporter>();
    using var importHost = builder.Build();
    var importLogger = importHost.Services.GetRequiredService<ILogger<Program>>();
    try
    {
        await using var scope = importHost.Services.CreateAsyncScope();
        // "latest": the newest scored run (run ids sort by date), for the Aspire resource that has no argument to edit.
        var importId = importRunId == "latest"
            ? (Directory.Exists(runsDirectory) ? Directory.GetDirectories(runsDirectory) : [])
                .Where(dir => File.Exists(Path.Combine(dir, EvaluationImporter.SummaryFile)))
                .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).LastOrDefault()
                ?? throw new FileNotFoundException($"No scored run in {runsDirectory}; run score.py and report.py first.")
            : importRunId;
        var imported = await scope.ServiceProvider.GetRequiredService<EvaluationImporter>().ImportAsync(Path.Combine(runsDirectory, importId));
        importLogger.LogInformation("Imported run {RunId} with {Pipelines} pipelines", imported.RunId, imported.Results.Count);
        return 0;
    }
    catch (FileNotFoundException ex)
    {
        importLogger.LogError("{Message}", ex.Message);
        return 1;
    }
}

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
builder.Services.AddSingleton<EvaluationScorer>();
builder.Services.AddScoped<EvaluationImporter>();

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

// The Ollama that answers (and judges, with --score). Recorded only when the chat model runs on Ollama.
var ollamaUrl = EvaluationScorer.OllamaUrl(builder.Configuration.GetConnectionString("ollama"));
var answeringEndpoint = string.Equals(chatProvider, "ollama", StringComparison.OrdinalIgnoreCase) ? ollamaUrl : null;

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
    // Latencies of one run must come from one machine: compared by host, since the Aspire Ollama container gets a new
    // local port on every start, and by the recorded hardware (it tells a local container from a native Ollama).
    var hardware = builder.Configuration["Evaluation:Hardware"];
    if ((info.OllamaEndpoint is not null && !string.Equals(EvaluationScorer.OllamaHost(info.OllamaEndpoint), EvaluationScorer.OllamaHost(answeringEndpoint), StringComparison.OrdinalIgnoreCase))
        || !string.Equals(info.Hardware, hardware, StringComparison.OrdinalIgnoreCase))
    {
        logger.LogError("Run {RunId} was answered by {Endpoint} ({Hardware}); resume it on the same machine, not {CurrentEndpoint} ({CurrentHardware})",
            runId, info.OllamaEndpoint, info.Hardware, answeringEndpoint, hardware);
        return 1;
    }
}
else
{
    info = new RunInfo(runId, DateTime.UtcNow, null, EvaluationArguments.GitCommit(repository), options.Seed ?? Random.Shared.Next(),
        chatProvider, chatModel, temperature, builder.Configuration["Ai:Embeddings:Model"] ?? "qwen3-embedding:0.6b",
        host.Services.GetRequiredService<QueryEmbeddingOptions>().Instruction, options.Pipelines, dataset.Questions.Count, dataset.DatasetHash, dataset.CorpusHash,
        // Where the models ran: latencies are only comparable between runs on the same hardware.
        answeringEndpoint, builder.Configuration["Evaluation:Hardware"]);
}

logger.LogInformation("Run {RunId}: {Questions} questions × {Pipelines} with {Model} (seed {Seed}) → {Directory}",
    runId, dataset.Questions.Count, string.Join(", ", info.Pipelines), chatModel, info.Seed, runDirectory);
await host.Services.GetRequiredService<EvaluationRunner>()
    .RunAsync(dataset, new EvaluationRunSettings(runDirectory, info.Pipelines, info.Seed, info), cancellation.Token);
if (!options.Score)
{
    logger.LogInformation("Run {RunId} finished. Score it with: cd eval/ragas && uv run python score.py ../runs/{RunId}", runId, runId);
    return 0;
}

if (!await host.Services.GetRequiredService<EvaluationScorer>().ScoreAsync(repository, runDirectory, ollamaUrl, cancellation.Token))
{
    return 1;
}

await using (var scope = host.Services.CreateAsyncScope())
{
    var imported = await scope.ServiceProvider.GetRequiredService<EvaluationImporter>().ImportAsync(runDirectory, cancellation.Token);
    logger.LogInformation("Run {RunId} scored and imported ({Pipelines} pipelines): see Métricas → Evaluación offline", imported.RunId, imported.Results.Count);
}
return 0;

public partial class Program;
