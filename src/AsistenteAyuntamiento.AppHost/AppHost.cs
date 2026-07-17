var builder = DistributedApplication.CreateBuilder(args);

// ── Auth0 secrets ─────────────────────────────────────────────────────────────
// Stored in user-secrets on AppHost (dev) or an external secrets store (prod).
// dotnet user-secrets set "Parameters:auth0-domain"         "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:auth0-client-id"      "..." --project src/AsistenteAyuntamiento.AppHost
// dotnet user-secrets set "Parameters:auth0-client-secret"  "..." --project src/AsistenteAyuntamiento.AppHost
var auth0Domain       = builder.AddParameter("auth0-domain",        secret: false);
var auth0ClientId     = builder.AddParameter("auth0-client-id",     secret: false);
var auth0ClientSecret = builder.AddParameter("auth0-client-secret", secret: true);

// ── Cloudflare R2 secrets (Optional) ────────────────────────────────────────
// To use R2, set these in user-secrets or appsettings.json instead of parameters.
// If missing, it will fallback to the local Azurite emulator.
var blobEndpoint = builder.Configuration["Blob:Endpoint"];
var blobAccessKeyId = builder.Configuration["Blob:AccessKeyId"];
var blobSecretAccessKey = builder.Configuration["Blob:SecretAccessKey"];
var blobBucketName = builder.Configuration["Blob:BucketName"];

var blobStorage = builder.AddAzureStorage("storage").RunAsEmulator();
var blobs = blobStorage.AddBlobs("BlobStorage");

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
    .WithReference(blobs)
    .WaitFor(apiService)
    // Auth0 — injected as environment variables (ASP.NET Core config key format: __ = :)
    .WithEnvironment("Auth0__Domain",        auth0Domain)
    .WithEnvironment("Auth0__ClientId",      auth0ClientId)
    .WithEnvironment("Auth0__ClientSecret",  auth0ClientSecret)
    .WithEnvironment("Auth0__Audience",      auth0Audience)
    // Cloudflare R2 / S3 (Only injected if configured, otherwise uses Azurite via Reference)
    .WithEnvironment("Blob__Endpoint",         blobEndpoint ?? "")
    .WithEnvironment("Blob__AccessKeyId",      blobAccessKeyId ?? "")
    .WithEnvironment("Blob__SecretAccessKey",  blobSecretAccessKey ?? "")
    .WithEnvironment("Blob__BucketName",       blobBucketName ?? "");

builder.Build().Run();
