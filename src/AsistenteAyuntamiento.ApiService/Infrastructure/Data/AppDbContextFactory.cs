using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace AsistenteAyuntamiento.ApiService.Infrastructure.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        
        // At design time, we don't have the Aspire config, so we mock it or pass a simple one.
        // We just need a connection string to let EF Core build the model and migrations.
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=asistente-ayuntamiento-db;Username=postgres;Password=postgres", x => x.UseVector());

        // Mock IHttpContextAccessor for design time
        var mockHttpContextAccessor = new HttpContextAccessor();

        return new AppDbContext(optionsBuilder.Options, mockHttpContextAccessor);
    }
}
