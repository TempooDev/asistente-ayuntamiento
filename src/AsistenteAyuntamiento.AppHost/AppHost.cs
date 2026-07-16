var builder = DistributedApplication.CreateBuilder(args);

// ── Auth0 secrets ─────────────────────────────────────────────────────────────
// Stored in user-secrets on AppHost (dev) or an external secrets store (prod).
// dotnet user-secrets set "Parameters:auth0-domain"         "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:auth0-client-id"      "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:auth0-client-secret"  "..." --project src/AsistenteAyuntamiento.AppHost
var auth0Domain       = builder.AddParameter("auth0-domain",        secret: false);
var auth0ClientId     = builder.AddParameter("auth0-client-id",     secret: false);
var auth0ClientSecret = builder.AddParameter("auth0-client-secret", secret: true);

// ── Cloudflare R2 secrets ─────────────────────────────────────────────────────
// dotnet user-secrets set "Parameters:blob-endpoint"          "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:blob-access-key-id"     "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:blob-secret-access-key" "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:blob-bucket-name"       "..." --project src/AsistenteAyuntamiento.AppHost
var blobEndpoint        = builder.AddParameter("blob-endpoint",          secret: false);
var blobAccessKeyId     = builder.AddParameter("blob-access-key-id",     secret: false);
var blobSecretAccessKey = builder.AddParameter("blob-secret-access-key", secret: true);
var blobBucketName      = builder.AddParameter("blob-bucket-name",       secret: false);

var auth0Audience     = builder.AddParameter("auth0-audience",      secret: false);

// ── Services ──────────────────────────────────────────────────────────────────
var apiService = builder.AddProject<Projects.AsistenteAyuntamiento_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Auth0__Domain",   auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience);

builder.AddProject<Projects.AsistenteAyuntamiento_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    // Auth0 — injected as environment variables (ASP.NET Core config key format: __ = :)
    .WithEnvironment("Auth0__Domain",        auth0Domain)
    .WithEnvironment("Auth0__ClientId",      auth0ClientId)
    .WithEnvironment("Auth0__ClientSecret",  auth0ClientSecret)
    .WithEnvironment("Auth0__Audience",      auth0Audience)
    // Cloudflare R2
    .WithEnvironment("Blob__Endpoint",         blobEndpoint)
    .WithEnvironment("Blob__AccessKeyId",      blobAccessKeyId)
    .WithEnvironment("Blob__SecretAccessKey",  blobSecretAccessKey)
    .WithEnvironment("Blob__BucketName",       blobBucketName);

builder.Build().Run();
