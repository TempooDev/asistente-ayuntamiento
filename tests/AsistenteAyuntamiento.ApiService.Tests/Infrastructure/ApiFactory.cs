using AsistenteAyuntamiento.ApiService.Features.Notifications;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Features.Chat;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AsistenteAyuntamiento.ApiService.Tests.Infrastructure;

/// <summary>
/// Boots the real ApiService pipeline (auth, policies, tenant middleware, endpoints) with:
/// header-based test authentication, an isolated EF Core InMemory database, and no
/// background consumers. Runs without Docker or external services.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string ClaimsNamespace = "https://test.asistente";
    public const string RoleClaimType = ClaimsNamespace + "/roles";
    public const string InternalGrpcKey = "test-internal-key";

    private readonly string _databaseName = $"api-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("Auth0:Domain", "test.local");
        builder.UseSetting("Auth0:Audience", "https://api.test");
        builder.UseSetting("Auth0:CustomClaimsNamespace", ClaimsNamespace);
        builder.UseSetting("Internal:GrpcApiKey", InternalGrpcKey);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("ConnectionStrings:asistente-ayuntamiento-db", "Host=localhost;Database=unused;Username=unused;Password=unused");
        builder.UseSetting("ConnectionStrings:qdrant", "Endpoint=http://localhost:6334;Key=unused");
        builder.UseSetting("ConnectionStrings:messaging", "amqp://guest:guest@localhost:5672");

        builder.ConfigureTestServices(services =>
        {
            ReplaceDatabase(services);

            services.RemoveAll<IHostedService>(typeof(RabbitMqNotificationConsumer));
            services.RemoveAll<IHostedService>(typeof(ChatPersistenceWorker));

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    private void ReplaceDatabase(IServiceCollection services)
    {
        var dbContextRegistrations = services
            .Where(d => d.ServiceType == typeof(AppDbContext)
                     || (d.ServiceType.IsGenericType && d.ServiceType.GenericTypeArguments.Contains(typeof(AppDbContext))))
            .ToList();

        foreach (var descriptor in dbContextRegistrations)
        {
            services.Remove(descriptor);
        }

        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
    }

    /// <summary>Runs <paramref name="seed"/> against the test database, bypassing tenant filters on read.</summary>
    public async Task SeedAsync(Func<AppDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seed(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    public HttpClient CreateClientAs(string? userId = null, string? orgId = null, params string[] roles)
    {
        var client = CreateClient();
        if (userId is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        if (orgId is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.OrgHeader, orgId);
        if (roles.Length > 0) client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        return client;
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<TService>(this IServiceCollection services, Type implementationType)
    {
        var matches = services
            .Where(d => d.ServiceType == typeof(TService) && d.ImplementationType == implementationType)
            .ToList();

        foreach (var descriptor in matches)
        {
            services.Remove(descriptor);
        }
    }
}
