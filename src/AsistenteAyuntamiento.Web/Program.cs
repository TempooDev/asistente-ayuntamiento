
using Amazon.S3;
using AsistenteAyuntamiento.Web;
using AsistenteAyuntamiento.Web.Client;
using AsistenteAyuntamiento.Web.Components;
using AsistenteAyuntamiento.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// ── Razor Components: Server + WASM ──────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization(); // Serialize ClaimsPrincipal for WASM handoff

// ── Output Cache ──────────────────────────────────────────────────────────────
builder.Services.AddOutputCache();

// ── Auth0 OIDC ────────────────────────────────────────────────────────────────
builder.Services.AddCascadingAuthenticationState();

// Auth0 config — required in production; in development use dotnet user-secrets
var auth0Domain = builder.Configuration["Auth0:Domain"] ?? "";
var auth0ClientId = builder.Configuration["Auth0:ClientId"] ?? "";
var auth0ClientSecret = builder.Configuration["Auth0:ClientSecret"] ?? "";

if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(auth0Domain))
        throw new InvalidOperationException("Auth0:Domain configuration is missing.");
    if (string.IsNullOrWhiteSpace(auth0ClientId))
        throw new InvalidOperationException("Auth0:ClientId configuration is missing.");
    if (string.IsNullOrWhiteSpace(auth0ClientSecret))
        throw new InvalidOperationException("Auth0:ClientSecret configuration is missing.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = $"https://{auth0Domain}";
        options.ClientId = auth0ClientId;
        options.ClientSecret = auth0ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.CallbackPath = "/callback";
        options.SaveTokens = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "https://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    });

builder.Services.AddAuthorization();

// ── Shared client services (HttpClient, WeatherApiClient, etc.) ───────────────
builder.Services.AddClientServices(builder.Configuration);

// ── Blob Storage ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IBlobStorageRepository>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var config = sp.GetRequiredService<IConfiguration>();

    if (env.IsDevelopment())
    {
        // Use Azurite emulator in development (connection string injected by Aspire)
        var connectionString = config.GetConnectionString("BlobStorage") ?? "UseDevelopmentStorage=true";
        return new AzuriteBlobStorageRepository(connectionString);
    }
    else
    {
        // Use Cloudflare R2 in production
        var endpoint = config["Blob:Endpoint"]
            ?? throw new InvalidOperationException("Blob:Endpoint environment variable is missing.");
        var accessKeyId = config["Blob:AccessKeyId"]
            ?? throw new InvalidOperationException("Blob:AccessKeyId environment variable is missing.");
        var secretAccessKey = config["Blob:SecretAccessKey"]
            ?? throw new InvalidOperationException("Blob:SecretAccessKey environment variable is missing.");
        var bucketName = config["Blob:BucketName"]
            ?? throw new InvalidOperationException("Blob:BucketName environment variable is missing.");

        var s3Config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
        };
        var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKeyId, secretAccessKey);
        var s3Client = new AmazonS3Client(credentials, s3Config);
        return new S3BlobStorageRepository(s3Client, bucketName);
    }
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
