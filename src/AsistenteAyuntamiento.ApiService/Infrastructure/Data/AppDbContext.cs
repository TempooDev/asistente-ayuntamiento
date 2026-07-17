using AsistenteAyuntamiento.ApiService.Features.Users;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.ApiService.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
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
    }
}
