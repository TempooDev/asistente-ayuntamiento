using AsistenteAyuntamiento.ApiService.Features.Tenants;
using AsistenteAyuntamiento.ApiService.Features.Users;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.ApiService.Infrastructure.Data;

public class AppDbContext : DbContext
{
    private readonly CurrentTenantService _tenantService;

    public AppDbContext(DbContextOptions<AppDbContext> options, CurrentTenantService tenantService) : base(options)
    {
        _tenantService = tenantService;
    }

    public DbSet<UserProfile> UserProfiles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Asignar esquema por Bounded Context (DDD)
        modelBuilder.HasDefaultSchema("identity");
        
        modelBuilder.Entity<UserProfile>()
            .HasIndex(u => u.Auth0UserId)
            .IsUnique();

        modelBuilder.Entity<UserProfile>()
            .HasQueryFilter(u => u.TenantId == _tenantService.TenantId);
    }
}
