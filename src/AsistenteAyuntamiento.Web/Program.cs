using Amazon.S3;
using AsistenteAyuntamiento.Web.Client;
using Microsoft.AspNetCore.Authentication;
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

builder.Services.Configure<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>(
    Auth0Constants.AuthenticationScheme,
    options =>
    {
        options.TokenValidationParameters.RoleClaimType = "https://asistente.ayuntamiento.com/roles";
    });

builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
    options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/Error"; // Or wherever you want to redirect access denied
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpForwarder();

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

app.Use(async (context, next) =>
{
    var token = await context.GetTokenAsync("access_token");
    if (!string.IsNullOrEmpty(token))
    {
        var tokenProvider = context.RequestServices.GetRequiredService<AppTokenProvider>();
        tokenProvider.AccessToken = token;
    }
    await next();
});

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(AsistenteAyuntamiento.Web.Client._Imports).Assembly);

app.MapGet("/login", async (HttpContext httpContext, string returnUrl = "/") =>
{
    var authenticationProperties = new LoginAuthenticationPropertiesBuilder()
        .WithRedirectUri(returnUrl)
        .Build();

    await httpContext.ChallengeAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
});

app.MapGet("/logout", async (HttpContext httpContext, string returnUrl = "/") =>
{
    var authenticationProperties = new LogoutAuthenticationPropertiesBuilder()
        .WithRedirectUri(returnUrl)
        .Build();

    await httpContext.SignOutAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
});

app.MapGet("/debug-claims", (System.Security.Claims.ClaimsPrincipal user) =>
{
    return Results.Ok(user.Claims.Select(c => new { c.Type, c.Value }));
}).RequireAuthorization();

app.MapForwarder("/chathub/{**catch-all}", "https+http://apiservice", new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(100) });
app.MapForwarder("/weatherforecast", "https+http://apiservice");
app.MapForwarder("/api/users/{**catch-all}", "https+http://apiservice");

app.MapDefaultEndpoints();

app.Run();
