using Microsoft.EntityFrameworkCore;

using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Arena;

namespace AsistenteAyuntamiento.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, AsistenteAyuntamiento.Application.Common.Interfaces.ICurrentTenantService tenantService) : DbContext(options), AsistenteAyuntamiento.Application.Common.Interfaces.IAppDbContext
{
    private readonly AsistenteAyuntamiento.Application.Common.Interfaces.ICurrentTenantService _tenantService = tenantService;

    public string? CurrentTenantId => _tenantService.TenantId;

    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<UserPreference> UserPreferences { get; set; }
    public DbSet<ChatSession> ChatSessions { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<AiCallLog> AiCallLogs { get; set; }
    public DbSet<AiConfiguration> AiConfigurations { get; set; }
    public DbSet<DocumentChunk> DocumentChunks { get; set; }
    public DbSet<DocumentJobState> DocumentJobStates { get; set; }
    public DbSet<ScraperFilterRule> ScraperFilterRules { get; set; }
    public DbSet<ParentDocument> ParentDocuments { get; set; }
    public DbSet<ChildFragment> ChildFragments { get; set; }
    public DbSet<ArenaBattle> ArenaBattles { get; set; }
    public DbSet<IngestionMetric> IngestionMetrics { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DocumentChunk>().ToTable("DocumentChunks", "ingestion");

        // Habilitar pgvector

        // Asignar esquema por Bounded Context (DDD)
        modelBuilder.HasDefaultSchema("identity");


        modelBuilder.Entity<UserProfile>()
            .HasIndex(u => u.Auth0UserId)
            .IsUnique();

        modelBuilder.Entity<UserProfile>()
            .HasQueryFilter(u => u.TenantId == CurrentTenantId);

        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.ToTable("UserPreferences", "identity");
            entity.HasQueryFilter(p => p.TenantId == CurrentTenantId);
            entity.HasIndex(p => p.Auth0UserId).IsUnique();
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.ToTable("ChatSessions", "chat");
            entity.HasQueryFilter(s => s.TenantId == CurrentTenantId);
            entity.HasMany(s => s.Messages).WithOne(m => m.Session).HasForeignKey(m => m.SessionId);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("ChatMessages", "chat");
            entity.HasQueryFilter(m => m.Session.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AiCallLog>(entity =>
        {
            entity.ToTable("AiCallLogs", "chat");
            entity.HasQueryFilter(c => c.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AiConfiguration>(entity =>
        {
            entity.ToTable("AiConfigurations", "identity");
            entity.HasQueryFilter(c => c.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ScraperFilterRule>(entity =>
        {
            entity.ToTable("ScraperFilterRules", "scraper");
            // Not adding tenant filter because scraping is global, according to domain rules
        });

        // === RAG Question Arena entities ===

        modelBuilder.Entity<ParentDocument>(entity =>
        {
            entity.ToTable("ParentDocuments", "ingestion");
            entity.Property(e => e.Metadata).HasColumnType("jsonb");
            entity.HasMany(e => e.Children).WithOne(e => e.Parent).HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
            // One version per document: re-ingestion replaces it (BaseHierarchicalIngestionProcessor).
            entity.HasIndex(e => e.DocumentId).IsUnique();
        });

        modelBuilder.Entity<DocumentJobState>(entity =>
        {
            // Stored as text: same values as the former string constants, so no data migration is needed.
            entity.Property(e => e.Status).HasConversion<string>();
        });

        modelBuilder.Entity<ChildFragment>(entity =>
        {
            entity.ToTable("ChildFragments", "ingestion");
            entity.HasIndex(e => e.ParentId);

            // TsvContent is filled by a database trigger and only exists on PostgreSQL. Non-relational
            // providers (EF InMemory in API tests) cannot map NpgsqlTsVector, so the column is skipped there.
            // TODO(audit-remediation 5.6): turn it into a shadow property and drop Npgsql from Domain.
            if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                entity.Property(e => e.TsvContent).HasColumnType("tsvector");
                entity.HasIndex(e => e.TsvContent).HasMethod("gin");
            }
            else
            {
                entity.Ignore(e => e.TsvContent);
            }
        });

        modelBuilder.Entity<ArenaBattle>(entity =>
        {
            entity.ToTable("ArenaBattles", "arena");
            entity.HasQueryFilter(b => b.TenantId == CurrentTenantId);
            entity.HasIndex(b => new { b.TenantId, b.UserId, b.SessionId });
        });

        modelBuilder.Entity<IngestionMetric>(entity =>
        {
            entity.ToTable("IngestionMetrics", "ingestion");
        });
    }
}

