using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace AsistenteAyuntamiento.Gateway.Telemetry;

/// <summary>
/// Grafana Faro from the browser, without exposing the Collector: the SPA posts to <c>/telemetry/faro</c> on its own
/// origin, and the gateway forwards to the Collector's Faro receiver (over the private channel) with the Collector
/// token. Cookies are never forwarded, requests are rate limited per client IP and their size is capped.
/// </summary>
/// <remarks>
/// Settings (<c>Telemetry:Faro</c>): <c>CollectorUrl</c> (empty = Faro disabled), <c>CollectorToken</c>,
/// <c>Environment</c> and <c>SampleRate</c> (0–1, share of sessions that send data).
/// </remarks>
public static class FaroForwarding
{
    public const string CollectPath = "/telemetry/faro";
    public const string ConfigPath = "/telemetry/config";
    public const string RateLimitPolicy = "faro";
    public const int MaxBodyBytes = 256 * 1024;
    public const int PermitsPerMinute = 120;

    /// <summary>Cap for all clients together, in case the client IP can be spoofed (gateway port reachable directly).</summary>
    public const int TotalPermitsPerMinute = 6000;

    public static WebApplicationBuilder AddFaroForwarding(this WebApplicationBuilder builder)
    {
        var collectorUrl = builder.Configuration["Telemetry:Faro:CollectorUrl"];
        if (!string.IsNullOrWhiteSpace(collectorUrl))
        {
            var route = new Dictionary<string, string?>
            {
                ["ReverseProxy:Routes:faro-route:ClusterId"] = "faro-cluster",
                ["ReverseProxy:Routes:faro-route:Order"] = "0",
                ["ReverseProxy:Routes:faro-route:Match:Path"] = CollectPath,
                ["ReverseProxy:Routes:faro-route:Match:Methods:0"] = "POST",
                ["ReverseProxy:Routes:faro-route:RateLimiterPolicy"] = RateLimitPolicy,
                ["ReverseProxy:Routes:faro-route:MaxRequestBodySize"] = MaxBodyBytes.ToString(CultureInfo.InvariantCulture),
                ["ReverseProxy:Routes:faro-route:Transforms:0:PathSet"] = "/",
                // Same-origin requests carry the session cookies and the user's token: none of it leaves the gateway.
                ["ReverseProxy:Routes:faro-route:Transforms:1:RequestHeaderRemove"] = "Cookie",
                ["ReverseProxy:Routes:faro-route:Transforms:2:RequestHeaderRemove"] = "Authorization",
                ["ReverseProxy:Clusters:faro-cluster:Destinations:collector:Address"] = collectorUrl,
            };
            var token = builder.Configuration["Telemetry:Faro:CollectorToken"];
            if (!string.IsNullOrWhiteSpace(token))
            {
                route["ReverseProxy:Routes:faro-route:Transforms:3:RequestHeader"] = "Authorization";
                route["ReverseProxy:Routes:faro-route:Transforms:3:Set"] = $"Bearer {token}";
            }
            builder.Configuration.AddInMemoryCollection(route);
        }

        // Behind the platform proxy (Traefik), the client IP is the last X-Forwarded-For entry it appended.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.Equals(CollectPath, StringComparison.OrdinalIgnoreCase)
                    ? RateLimitPartition.GetFixedWindowLimiter("faro-total", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = TotalPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter("other"));
            options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress ?? IPAddress.None,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        return builder;
    }

    /// <summary>Call before <c>MapReverseProxy</c>.</summary>
    public static WebApplication UseFaroForwarding(this WebApplication app)
    {
        app.UseForwardedHeaders();

        // Faro sends small JSON batches with a Content-Length; anything else is refused before it is proxied
        // (the route's MaxRequestBodySize depends on the server, this does not).
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals(CollectPath, StringComparison.OrdinalIgnoreCase)
                && context.Request.ContentLength is not (> 0 and <= MaxBodyBytes))
            {
                context.Response.StatusCode = context.Request.ContentLength is null
                    ? StatusCodes.Status411LengthRequired
                    : StatusCodes.Status413PayloadTooLarge;
                return;
            }
            await next(context);
        });

        app.UseRateLimiter();

        app.MapGet(ConfigPath, (IConfiguration configuration) =>
        {
            var enabled = !string.IsNullOrWhiteSpace(configuration["Telemetry:Faro:CollectorUrl"]);
            var sampleRate = Math.Clamp(configuration.GetValue("Telemetry:Faro:SampleRate", 1.0), 0, 1);
            return Results.Ok(new FaroClientConfig(
                enabled,
                enabled ? CollectPath : null,
                configuration["Telemetry:Faro:Environment"] ?? app.Environment.EnvironmentName.ToLowerInvariant(),
                sampleRate));
        });

        return app;
    }
}

/// <summary>What the SPA needs to start Faro (no secrets).</summary>
public sealed record FaroClientConfig(bool Enabled, string? CollectorUrl, string Environment, double SampleRate);
