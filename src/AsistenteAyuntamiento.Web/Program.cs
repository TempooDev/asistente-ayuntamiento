using Amazon.S3;
using AsistenteAyuntamiento.Web.Client;
using Microsoft.AspNetCore.Authentication;
using AsistenteAyuntamiento.Web.Components;
using AsistenteAyuntamiento.Web.Infrastructure;
using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// ── Razor Components: Server + WASM ──────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization(); // Serializa ClaimsPrincipal para el handoff SSR → WASM

// ── Output Cache ──────────────────────────────────────────────────────────────
builder.Services.AddOutputCache();

// ── HTTP Forwarder (Proxy for WASM) ──────────────────────────────────────────
builder.Services.AddHttpForwarderWithServiceDiscovery();

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
        options.SaveTokens = true;
        var previousOnTokenValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = context =>
        {
            var accessToken = context.TokenEndpointResponse?.AccessToken;
            if (!string.IsNullOrEmpty(accessToken))
            {
                if (context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    identity.AddClaim(new System.Security.Claims.Claim("access_token", accessToken));
                }
            }
            
            if (previousOnTokenValidated != null)
            {
                return previousOnTokenValidated(context);
            }
            return Task.CompletedTask;
        };
    });

builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme,
    options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/Error"; // Or wherever you want to redirect access denied
    });

builder.Services.AddAuthorization();

// ── Shared client services (HttpClient, WeatherApiClient, etc.) ───────────────
builder.Services.AddClientServices(builder.Configuration);

// Override BaseAddress for SSR to bypass the gateway and talk directly to the API
builder.Services.AddHttpClient<WeatherApiClient>(c => c.BaseAddress = new Uri("http://apiservice"));
builder.Services.AddHttpClient<UserApiClient>(c => c.BaseAddress = new Uri("http://apiservice"));
builder.Services.AddHttpClient<AiConfigApiClient>(c => c.BaseAddress = new Uri("http://apiservice"));
builder.Services.AddHttpClient<IngestionApiClient>(c =>
{
    c.BaseAddress = new Uri("http://apiservice");
    c.Timeout = TimeSpan.FromMinutes(10);
});

// SignalR hub URL — server connects directly to apiservice (bypasses gateway)
builder.Services.Configure<ChatHubOptions>(o => o.HubUrl = "http://apiservice/hubs/chat");

// ── Blob Storage ──────────────────────────────────────────────────────────────
// Aspire inyecta las credenciales de R2/MinIO como env vars (Blob__*).
builder.Services.AddSingleton<IBlobStorageRepository>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["Blob:Endpoint"]
        ?? throw new InvalidOperationException("Blob:Endpoint is required.");

    // Cloudflare R2 / MinIO (o cualquier endpoint S3-compatible)
    var accessKeyId = config["Blob:AccessKeyId"]
        ?? throw new InvalidOperationException("Blob:AccessKeyId is required when Blob:Endpoint is set.");
    var secretAccessKey = config["Blob:SecretAccessKey"]
        ?? throw new InvalidOperationException("Blob:SecretAccessKey is required when Blob:Endpoint is set.");
    var bucketName = config["Blob:BucketName"]
        ?? throw new InvalidOperationException("Blob:BucketName is required when Blob:Endpoint is set.");

    var s3Config = new AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true };
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
        Console.WriteLine($"[Middleware] Token found! Length: {token.Length}");
        var tokenProvider = context.RequestServices.GetRequiredService<AppTokenProvider>();
        tokenProvider.AccessToken = token;
    }
    else
    {
        Console.WriteLine("[Middleware] Token is null or empty!");
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

app.MapDefaultEndpoints();

app.UseWebSockets();

// Forward /api and /hubs to apiservice using Service Discovery
app.MapForwarder("/api/{**catch-all}", "http://apiservice", transformBuilder =>
{
    transformBuilder.AddRequestTransform(async transformContext =>
    {
        var token = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.GetTokenAsync(transformContext.HttpContext, "access_token");
        if (!string.IsNullOrEmpty(token))
        {
            transformContext.ProxyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    });
});

app.MapForwarder("/hubs/{**catch-all}", "http://apiservice", transformBuilder =>
{
    transformBuilder.AddRequestTransform(async transformContext =>
    {
        var token = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.GetTokenAsync(transformContext.HttpContext, "access_token");
        if (!string.IsNullOrEmpty(token))
        {
            transformContext.ProxyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    });
});

app.Run();
