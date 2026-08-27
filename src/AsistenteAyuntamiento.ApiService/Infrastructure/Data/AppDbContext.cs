using AsistenteAyuntamiento.ApiService.Features.Tenants;
using AsistenteAyuntamiento.ApiService.Features.Users;
using AsistenteAyuntamiento.ApiService.Features.AiConfig;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using AsistenteAyuntamiento.ApiService.Features.Ingestion;

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
    public DbSet<Features.Chat.ChatSession> ChatSessions { get; set; }
    public DbSet<Features.Chat.ChatMessage> ChatMessages { get; set; }
    public DbSet<Features.Chat.AiCallLog> AiCallLogs { get; set; }
    public DbSet<AiConfiguration> AiConfigurations { get; set; }
    public DbSet<DocumentChunk> DocumentChunks { get; set; }
    public DbSet<DocumentJobState> DocumentJobStates { get; set; }
    public DbSet<Features.Scraper.ScraperFilterRule> ScraperFilterRules { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Habilitar pgvector
        modelBuilder.HasPostgresExtension("vector");

        // Asignar esquema por Bounded Context (DDD)
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<DocumentChunk>()
            .Property(c => c.Embedding)
            .HasColumnType("vector(768)");

        modelBuilder.Entity<UserProfile>()
            .HasIndex(u => u.Auth0UserId)
            .IsUnique();

        modelBuilder.Entity<UserProfile>()
            .HasQueryFilter(u => u.TenantId == CurrentTenantId);

        modelBuilder.Entity<Features.Chat.ChatSession>(entity =>
        {
            entity.ToTable("ChatSessions", "chat");
            entity.HasQueryFilter(s => s.TenantId == CurrentTenantId);
            entity.HasMany(s => s.Messages).WithOne(m => m.Session).HasForeignKey(m => m.SessionId);
        });

        modelBuilder.Entity<Features.Chat.ChatMessage>(entity =>
        {
            entity.ToTable("ChatMessages", "chat");
            entity.HasQueryFilter(m => m.Session.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Features.Chat.AiCallLog>(entity =>
        {
            entity.ToTable("AiCallLogs", "chat");
            entity.HasQueryFilter(c => c.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AiConfiguration>(entity =>
        {
            entity.ToTable("AiConfigurations", "identity");
            entity.HasQueryFilter(c => c.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Features.Scraper.ScraperFilterRule>(entity =>
        {
            entity.ToTable("ScraperFilterRules", "scraper");
            // Not adding tenant filter because scraping is global, according to domain rules
        });
    }
}
