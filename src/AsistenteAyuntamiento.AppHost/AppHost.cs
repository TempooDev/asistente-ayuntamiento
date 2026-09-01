var builder = DistributedApplication.CreateBuilder(args);

// ── Auth0 secrets ─────────────────────────────────────────────────────────────
// Stored in user-secrets on AppHost (dev) or an external secrets store (prod).
// dotnet user-secrets set "Parameters:auth0-domain"         "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:auth0-client-id"      "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:auth0-client-secret"  "..." --project src/AsistenteAyuntamiento.AppHost
var auth0Domain = builder.AddParameter("auth0-domain", secret: false);
var auth0ClientId = builder.AddParameter("auth0-client-id", secret: false);
var auth0ClientSecret = builder.AddParameter("auth0-client-secret", secret: true);

// ── AI configuration ─────────────────────────────────────────────────────────
// Stored in user-secrets on AppHost for local dev
var aiChatProvider = builder.AddParameter("ai-chat-provider", secret: false);
var aiChatModel = builder.AddParameter("ai-chat-model", secret: false);
var aiChatApiKey = builder.AddParameter("ai-chat-api-key", secret: true);

var aiEmbeddingsProvider = builder.AddParameter("ai-embeddings-provider", secret: false);
var aiEmbeddingsModel = builder.AddParameter("ai-embeddings-model", secret: false);
var aiEmbeddingsApiKey = builder.AddParameter("ai-embeddings-api-key", secret: true);

// ── Cloudflare R2 secrets (Optional) ────────────────────────────────────────
// To use R2, set these in user-secrets or appsettings.json instead of parameters.
// If missing, it will fallback to the local Azurite emulator.
var blobEndpoint = builder.Configuration["Blob:Endpoint"];
var blobAccessKeyId = builder.Configuration["Blob:AccessKeyId"];
var blobSecretAccessKey = builder.Configuration["Blob:SecretAccessKey"];
var blobBucketName = builder.Configuration["Blob:BucketName"] ?? "boletines";

// Configure MinIO container
// Persistent MinIO Object Storage
var minio = builder.AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithVolume("minio-data", "/data")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithEnvironment("MINIO_ROOT_USER", "admin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "password123");

var minioEndpoint = minio.GetEndpoint("api");

// Init container to create bucket
var minioInit = builder.AddContainer("minio-init", "minio/mc")
    .WithEntrypoint("sh")
    .WithArgs("-c", "sleep 5; mc alias set myminio $MINIO_ENDPOINT admin password123; mc mb myminio/boletines || true; mc anonymous set public myminio/boletines || true")
    .WithEnvironment("MINIO_ENDPOINT", minioEndpoint)
    .WaitFor(minio);

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

var ollama = builder.AddOllama("ollama")
    .WithDataVolume();
ollama.AddModel("llama3.2");
ollama.AddModel("nomic-embed-text");

var apiService = builder.AddProject<Projects.AsistenteAyuntamiento_ApiService>("apiservice")
    .WithReference(ollama)
    .WithHttpHealthCheck("/health")
    .WithReference(db)
    .WithReference(rabbitmq)
    .WaitFor(db)
    .WaitFor(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience)
    .WithEnvironment("Ai__Chat__Provider", aiChatProvider)
    .WithEnvironment("Ai__Chat__Model", aiChatModel)
    .WithEnvironment("Ai__Chat__ApiKey", aiChatApiKey)
    .WithEnvironment("Ai__Embeddings__Provider", aiEmbeddingsProvider)
    .WithEnvironment("Ai__Embeddings__Model", aiEmbeddingsModel)
    .WithEnvironment("Ai__Embeddings__ApiKey", aiEmbeddingsApiKey);

if (!string.IsNullOrEmpty(blobEndpoint))
{
    apiService.WithEnvironment("Blob__Endpoint", blobEndpoint);
}
else
{
    apiService.WithEnvironment("Blob__Endpoint", minioEndpoint);
}

apiService.WithEnvironment("Blob__AccessKeyId", blobAccessKeyId ?? "admin")
          .WithEnvironment("Blob__SecretAccessKey", blobSecretAccessKey ?? "password123")
          .WithEnvironment("Blob__BucketName", blobBucketName);

var webfrontend = builder.AddNpmApp("webfrontend", "../AsistenteAyuntamiento.Angular", "start")
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithReference(db)
    .WaitFor(apiService)
    // Auth0 — injected as environment variables (Angular native NG_APP_ format)
    .WithEnvironment("NG_APP_AUTH0_DOMAIN", auth0Domain)
    .WithEnvironment("NG_APP_AUTH0_CLIENT_ID", auth0ClientId)
    .WithEnvironment("NG_APP_AUTH0_AUDIENCE", auth0Audience);

if (!string.IsNullOrEmpty(blobEndpoint))
{
    webfrontend.WithEnvironment("Blob__Endpoint", blobEndpoint);
}
else
{
    webfrontend.WithEnvironment("Blob__Endpoint", minioEndpoint);
}

webfrontend.WithEnvironment("Blob__AccessKeyId", blobAccessKeyId ?? "admin")
           .WithEnvironment("Blob__SecretAccessKey", blobSecretAccessKey ?? "password123")
           .WithEnvironment("Blob__BucketName", blobBucketName);

var gateway = builder.AddProject<Projects.AsistenteAyuntamiento_Gateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService)
    .WaitFor(webfrontend)
    .WithReference(webfrontend);

var goScraper = builder.AddGolangApp("go-scraper", "../go-scraper")
    .WithHttpEndpoint(targetPort: 8080, name: "http", env: "PORT")
    .WithHttpEndpoint(targetPort: 50051, name: "grpc", env: "GRPC_PORT")
    .WithHttpHealthCheck("/health")
    .WithReference(rabbitmq)
    .WithReference(apiService)
    .WithEnvironment("DOTNET_API_GRPC_URL", apiService.GetEndpoint("https"))
    .WaitFor(rabbitmq)
    .WaitFor(apiService);

if (!string.IsNullOrEmpty(blobEndpoint))
{
    goScraper.WithEnvironment("Blob__Endpoint", blobEndpoint);
}
else
{
    goScraper.WithEnvironment("Blob__Endpoint", minioEndpoint);
}

goScraper.WithEnvironment("Blob__AccessKeyId", blobAccessKeyId ?? "admin")
         .WithEnvironment("Blob__SecretAccessKey", blobSecretAccessKey ?? "password123")
         .WithEnvironment("Blob__BucketName", blobBucketName);

apiService.WithEnvironment("GoScraper__GrpcUrl", goScraper.GetEndpoint("grpc"));

var worker = builder.AddProject<Projects.AsistenteAyuntamiento_Worker>("worker")
    .WithReference(db)
    .WithReference(rabbitmq)
    .WithReference(ollama)
    .WaitFor(db)
    .WaitFor(rabbitmq)
    .WaitFor(ollama)
    .WithEnvironment("Ai__Chat__Provider", aiChatProvider)
    .WithEnvironment("Ai__Chat__Model", aiChatModel)
    .WithEnvironment("Ai__Chat__ApiKey", aiChatApiKey)
    .WithEnvironment("Ai__Embeddings__Provider", aiEmbeddingsProvider)
    .WithEnvironment("Ai__Embeddings__Model", aiEmbeddingsModel)
    .WithEnvironment("Ai__Embeddings__ApiKey", aiEmbeddingsApiKey);

if (!string.IsNullOrEmpty(blobEndpoint))
{
    worker.WithEnvironment("Blob__Endpoint", blobEndpoint);
}
else
{
    worker.WithEnvironment("Blob__Endpoint", minioEndpoint);
}

worker.WithEnvironment("Blob__AccessKeyId", blobAccessKeyId ?? "admin")
      .WithEnvironment("Blob__SecretAccessKey", blobSecretAccessKey ?? "password123")
      .WithEnvironment("Blob__BucketName", blobBucketName);

builder.Build().Run();
