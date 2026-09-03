using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AsistenteAyuntamiento.ApiService.Infrastructure.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=asistente-ayuntamiento-db;Username=postgres;Password=postgres", x => x.UseVector());

        return new AppDbContext(optionsBuilder.Options, new DummyTenantService());
    }
}

public class DummyTenantService : ICurrentTenantService
{
    public string TenantId => "design-time";
    public void SetTenant(string tenantId) {}
}
