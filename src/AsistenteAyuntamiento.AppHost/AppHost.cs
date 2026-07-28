var builder = DistributedApplication.CreateBuilder(args);

// ── Auth0 secrets ─────────────────────────────────────────────────────────────
// Stored in user-secrets on AppHost (dev) or an external secrets store (prod).
// dotnet user-secrets set "Parameters:auth0-domain"         "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:auth0-client-id"      "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:auth0-client-secret"  "..." --project src/AsistenteAyuntamiento.AppHost
var auth0Domain = builder.AddParameter("auth0-domain", secret: false);
var auth0ClientId = builder.AddParameter("auth0-client-id", secret: false);
var auth0ClientSecret = builder.AddParameter("auth0-client-secret", secret: true);

// ── Cloudflare R2 secrets (Optional) ────────────────────────────────────────
// To use R2, set these in user-secrets or appsettings.json instead of parameters.
// If missing, it will fallback to the local Azurite emulator.
var blobEndpoint = builder.Configuration["Blob:Endpoint"];
var blobAccessKeyId = builder.Configuration["Blob:AccessKeyId"];
var blobSecretAccessKey = builder.Configuration["Blob:SecretAccessKey"];
var blobBucketName = builder.Configuration["Blob:BucketName"];

var blobStorage = builder.AddAzureStorage("storage").RunAsEmulator();
var blobs = blobStorage.AddBlobs("boletines");

var auth0Audience = builder.AddParameter("auth0-audience", secret: false);

var postgresServer = builder.AddPostgres("postgres", port: 5432)
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg16")
    .WithDataVolume("asistente-ayuntamiento-pgdata-v2")
    .WithLifetime(ContainerLifetime.Persistent);

var db = postgresServer.AddDatabase("asistente-ayuntamiento-db");

var rabbitmq = builder.AddRabbitMQ("messaging")
    .WithDataVolume("asistente-ayuntamiento-rmqdata")
    .WithLifetime(ContainerLifetime.Persistent);

var ollama = builder.AddOllama("ollama");
ollama.AddModel("llama3.2");
ollama.AddModel("nomic-embed-text");

var apiService = builder.AddProject<Projects.AsistenteAyuntamiento_ApiService>("apiservice")
    .WithReference(ollama)
    .WithHttpHealthCheck("/health")
    .WithReference(db)
    .WithReference(rabbitmq)
    .WithReference(blobs)
    .WaitFor(db)
    .WaitFor(rabbitmq)
    .WaitFor(blobs)
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience);

var webfrontend = builder.AddProject<Projects.AsistenteAyuntamiento_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WithReference(db)
    .WithReference(blobs)
    .WaitFor(apiService)
    // Auth0 — injected as environment variables (ASP.NET Core config key format: __ = :)
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__ClientId", auth0ClientId)
    .WithEnvironment("Auth0__ClientSecret", auth0ClientSecret)
    .WithEnvironment("Auth0__Audience", auth0Audience)
    // Cloudflare R2 / S3 (Only injected if configured, otherwise uses Azurite via Reference)
    .WithEnvironment("Blob__Endpoint", blobEndpoint ?? "")
    .WithEnvironment("Blob__AccessKeyId", blobAccessKeyId ?? "")
    .WithEnvironment("Blob__SecretAccessKey", blobSecretAccessKey ?? "")
    .WithEnvironment("Blob__BucketName", blobBucketName ?? "");

var gateway = builder.AddProject<Projects.AsistenteAyuntamiento_Gateway>("gateway")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WaitFor(webfrontend)
    .WithReference(webfrontend);

var goScraper = builder.AddGolangApp("go-scraper", "../go-scraper")
    .WithHttpEndpoint(targetPort: 8080, name: "http", env: "PORT")
    .WithHttpHealthCheck("/health")
    .WithReference(blobs)
    .WithReference(rabbitmq)
    .WaitFor(blobs)
    .WaitFor(rabbitmq)
    .WithEnvironment("Blob__Endpoint", blobEndpoint ?? "")
    .WithEnvironment("Blob__AccessKeyId", blobAccessKeyId ?? "")
    .WithEnvironment("Blob__SecretAccessKey", blobSecretAccessKey ?? "")
    .WithEnvironment("Blob__BucketName", blobBucketName ?? "");

builder.Build().Run();
