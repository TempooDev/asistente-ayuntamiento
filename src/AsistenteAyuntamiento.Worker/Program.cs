using AsistenteAyuntamiento.ApiService.Features.Ingestion;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Required by AppDbContext even if it's always null in a worker
builder.Services.AddHttpContextAccessor();

// Configure Database
builder.AddNpgsqlDbContext<AppDbContext>(
    "asistente-ayuntamiento-db",
    configureDbContextOptions: options => options.UseNpgsql(npgsqlOptions => npgsqlOptions.UseVector()));

// Configure RabbitMQ
builder.AddRabbitMQClient("messaging");

// Configure S3/Blob
var blobEndpoint = builder.Configuration["Blob:Endpoint"];
if (!string.IsNullOrEmpty(blobEndpoint))
{
    var accessKeyId = builder.Configuration["Blob:AccessKeyId"] ?? "admin";
    var secretAccessKey = builder.Configuration["Blob:SecretAccessKey"] ?? "password123";
    var s3Config = new Amazon.S3.AmazonS3Config { ServiceURL = blobEndpoint, ForcePathStyle = true };
    var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKeyId, secretAccessKey);
    builder.Services.AddSingleton<Amazon.S3.IAmazonS3>(new Amazon.S3.AmazonS3Client(credentials, s3Config));
}

// Configure Semantic Kernel / Ollama
#pragma warning disable SKEXP0070 // Ollama connector is experimental
var ollamaConnString = builder.Configuration.GetConnectionString("ollama") ?? "http://localhost:11434";
var ollamaEndpoint = ollamaConnString.StartsWith("Endpoint=") 
    ? ollamaConnString.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length) 
    : ollamaConnString;

builder.Services.AddKernel()
#pragma warning disable SKEXP0001
    .AddOllamaTextEmbeddingGeneration("nomic-embed-text", new Uri(ollamaEndpoint));
#pragma warning restore SKEXP0001
#pragma warning restore SKEXP0070

// Register background services for ingestion
builder.Services.AddScoped<DocumentIngestionService>();
builder.Services.AddHostedService<RabbitMqConsumerService>();

var host = builder.Build();
host.Run();
