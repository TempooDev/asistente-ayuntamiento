using Amazon.S3;
using AsistenteAyuntamiento.Web.Client;
using AsistenteAyuntamiento.Web.Components;
using AsistenteAyuntamiento.Web.Infrastructure;
using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// ── Razor Components: Server + WASM ──────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization(); // Serializa ClaimsPrincipal para el handoff SSR → WASM

// ── Output Cache ──────────────────────────────────────────────────────────────
builder.Services.AddOutputCache();

// ── Auth0 OIDC ────────────────────────────────────────────────────────────────
// Los valores llegan como variables de entorno inyectadas por Aspire (AppHost.cs).
// En dev: desde user-secrets del AppHost. En prod: desde el secrets store externo.
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuth0WebAppAuthentication(options =>
{
    options.Domain = builder.Configuration["Auth0:Domain"]!;
    options.ClientId = builder.Configuration["Auth0:ClientId"]!;
    options.ClientSecret = builder.Configuration["Auth0:ClientSecret"];
    options.Scope = "openid profile email";
})
.WithAccessToken(tokenOptions =>
{
    var audience = builder.Configuration["Auth0:Audience"];
    if (!string.IsNullOrEmpty(audience))
    {
        tokenOptions.Audience = audience;
    }
});

builder.Services.AddAuthorization();

// ── Shared client services (HttpClient, WeatherApiClient, etc.) ───────────────
builder.Services.AddClientServices(builder.Configuration);

// ── Blob Storage ──────────────────────────────────────────────────────────────
// Aspire inyecta las credenciales de R2 como env vars (Blob__*).
// En desarrollo, si el endpoint no está configurado, se usa Azurite como fallback.
builder.Services.AddSingleton<IBlobStorageRepository>(sp =>
{
    var config   = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["Blob:Endpoint"];

    if (string.IsNullOrWhiteSpace(endpoint))
    {
        // Fallback: Azurite emulator (inyectado por Aspire via AddAzureStorage / Aspire.Hosting.Azure.Storage)
        var connectionString = config.GetConnectionString("BlobStorage") ?? "UseDevelopmentStorage=true";
        return new AzuriteBlobStorageRepository(connectionString);
    }

    // Cloudflare R2 (o cualquier endpoint S3-compatible)
    var accessKeyId     = config["Blob:AccessKeyId"]
        ?? throw new InvalidOperationException("Blob:AccessKeyId is required when Blob:Endpoint is set.");
    var secretAccessKey = config["Blob:SecretAccessKey"]
        ?? throw new InvalidOperationException("Blob:SecretAccessKey is required when Blob:Endpoint is set.");
    var bucketName      = config["Blob:BucketName"]
        ?? throw new InvalidOperationException("Blob:BucketName is required when Blob:Endpoint is set.");

    var s3Config    = new AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true };
    var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKeyId, secretAccessKey);
    return new S3BlobStorageRepository(new AmazonS3Client(credentials, s3Config), bucketName);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(AsistenteAyuntamiento.Web.Client._Imports).Assembly);

app.MapDefaultEndpoints();

app.Run();
