using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Arena;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.Evaluation;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Users;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace AsistenteAyuntamiento.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, AsistenteAyuntamiento.Application.Common.Interfaces.ICurrentTenantService tenantService) : DbContext(options), AsistenteAyuntamiento.Application.Common.Interfaces.IAppDbContext
{
    private readonly AsistenteAyuntamiento.Application.Common.Interfaces.ICurrentTenantService _tenantService = tenantService;

    public string? CurrentTenantId => _tenantService.TenantId;

    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<UserPreference> UserPreferences { get; set; }
    public DbSet<ChatSession> ChatSessions { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<FailedChatMessage> FailedChatMessages { get; set; }
    public DbSet<AiCallLog> AiCallLogs { get; set; }
    public DbSet<AiConfiguration> AiConfigurations { get; set; }
    public DbSet<DocumentChunk> DocumentChunks { get; set; }
    public DbSet<DocumentJobState> DocumentJobStates { get; set; }
    public DbSet<ScraperFilterRule> ScraperFilterRules { get; set; }
    public DbSet<ParentDocument> ParentDocuments { get; set; }
    public DbSet<ChildFragment> ChildFragments { get; set; }
    public DbSet<ArenaBattle> ArenaBattles { get; set; }
    public DbSet<IngestionMetric> IngestionMetrics { get; set; }
    public DbSet<EvaluationRun> EvaluationRuns { get; set; }
    public DbSet<EvaluationPipelineResult> EvaluationPipelineResults { get; set; }

    /// <summary>Full-text column of <c>ingestion."ChildFragments"</c> (shadow property, maintained by a trigger).</summary>
    public const string ChildFragmentTsvColumn = "TsvContent";

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational())
        {
            return operation(cancellationToken);
        }

        var strategy = Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await Database.BeginTransactionAsync(ct);
            await operation(ct);
            await transaction.CommitAsync(ct);
        }, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.ToTable("DocumentChunks", "ingestion");
            // Rows from before the chunking study are the baseline's; each variant replaces only its own rows.
            entity.Property(c => c.ChunkingStrategy).HasDefaultValue(DocumentChunk.FixedOverlapStrategy);
            entity.HasIndex(c => new { c.DocumentId, c.ChunkingStrategy });
        });

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
            // One set of preferences per user and organization.
            entity.HasIndex(p => new { p.Auth0UserId, p.TenantId }).IsUnique();
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

        // Dead letters of ChatPersistenceWorker: no foreign key (the session may be gone) and no tenant filter
        // (operator-only, not exposed by any endpoint).
        modelBuilder.Entity<FailedChatMessage>(entity =>
        {
            entity.ToTable("FailedChatMessages", "chat");
            entity.Property(m => m.Id).ValueGeneratedNever();
            entity.Property(m => m.Role).HasMaxLength(20);
            entity.Property(m => m.Error).HasMaxLength(300);
            entity.HasIndex(m => m.FailedAt);
        });

        modelBuilder.Entity<AiCallLog>(entity =>
        {
            entity.ToTable("AiCallLogs", "chat");
            entity.HasQueryFilter(c => c.TenantId == CurrentTenantId);
            entity.Property(c => c.Operation).HasDefaultValue("answer");
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
            entity.Property(e => e.BaselineStatus).HasConversion<string>();
            entity.Property(e => e.HierarchicalStatus).HasConversion<string>();
            entity.Property(e => e.RecursiveStatus).HasConversion<string>();
            entity.Property(e => e.SemanticStatus).HasConversion<string>();
            // Every worker updates the same row: PostgreSQL's xmin detects a concurrent change and the update is retried.
            entity.Property<uint>("Version").IsRowVersion();
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<ChildFragment>(entity =>
        {
            entity.ToTable("ChildFragments", "ingestion");
            entity.HasIndex(e => e.ParentId);

            // TsvContent is filled by a database trigger and only exists on PostgreSQL: a shadow property,
            // so the Domain stays free of Npgsql types. Non-relational providers (EF InMemory in tests) skip it.
            if (Database.IsNpgsql())
            {
                entity.Property<NpgsqlTsVector>(ChildFragmentTsvColumn).HasColumnType("tsvector");
                entity.HasIndex(ChildFragmentTsvColumn).HasMethod("gin");
            }
        });

        modelBuilder.Entity<ArenaBattle>(entity =>
        {
            entity.ToTable("ArenaBattles", "arena");
            entity.HasQueryFilter(b => b.TenantId == CurrentTenantId);
            // xmin: two simultaneous votes on the same battle cannot both be saved.
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasIndex(b => new { b.TenantId, b.UserId, b.SessionId });
        });

        modelBuilder.Entity<IngestionMetric>(entity =>
        {
            entity.ToTable("IngestionMetrics", "ingestion");
        });

        // Offline evaluation results: platform-wide, no tenant filter.
        modelBuilder.Entity<EvaluationRun>(entity =>
        {
            entity.ToTable("EvaluationRuns", "evaluation");
            entity.HasMany(r => r.Results).WithOne().HasForeignKey(p => p.RunId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EvaluationPipelineResult>(entity =>
        {
            entity.ToTable("EvaluationPipelineResults", "evaluation");
            entity.HasKey(p => new { p.RunId, p.Pipeline });
        });
    }
}

