using AsistenteAyuntamiento.ApiService.Features.Tenants;
using AsistenteAyuntamiento.ApiService.Features.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
namespace AsistenteAyuntamiento.ApiService.Infrastructure.Data;

public class AppDbContext : DbContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor httpContextAccessor) : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string CurrentTenantId => _httpContextAccessor.HttpContext?.RequestServices.GetService<CurrentTenantService>()?.TenantId ?? "default";

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
            .HasQueryFilter(u => u.TenantId == CurrentTenantId);
    }
}
