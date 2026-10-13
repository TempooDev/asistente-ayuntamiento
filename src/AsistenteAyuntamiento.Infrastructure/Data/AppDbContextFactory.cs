using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AsistenteAyuntamiento.Infrastructure.Data;

/// <summary>
/// Used by <c>dotnet ef</c> only. The connection string comes from <c>ConnectionStrings__asistente-ayuntamiento-db</c>
/// (or <c>DESIGN_TIME_CONNECTION_STRING</c>); adding a migration needs no database, applying one does.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string LocalPlaceholder = "Host=localhost;Port=5432;Database=asistente-ayuntamiento-db";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__asistente-ayuntamiento-db")
            ?? Environment.GetEnvironmentVariable("DESIGN_TIME_CONNECTION_STRING")
            ?? LocalPlaceholder;

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString, x => x.UseVector());

        return new AppDbContext(optionsBuilder.Options, new DummyTenantService());
    }
}

public class DummyTenantService : ICurrentTenantService
{
    public string TenantId => "design-time";
    public void SetTenant(string tenantId) { }
}
