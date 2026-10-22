using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.Evaluation;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Users;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<UserProfile> UserProfiles { get; set; }
    DbSet<UserPreference> UserPreferences { get; set; }
    DbSet<ChatSession> ChatSessions { get; set; }
    DbSet<ChatMessage> ChatMessages { get; set; }
    DbSet<FailedChatMessage> FailedChatMessages { get; set; }
    DbSet<AiCallLog> AiCallLogs { get; set; }
    DbSet<AiConfiguration> AiConfigurations { get; set; }
    DbSet<DocumentChunk> DocumentChunks { get; set; }
    DbSet<DocumentJobState> DocumentJobStates { get; set; }
    DbSet<ScraperFilterRule> ScraperFilterRules { get; set; }
    DbSet<AsistenteAyuntamiento.Domain.Features.Arena.ArenaBattle> ArenaBattles { get; set; }
    DbSet<ParentDocument> ParentDocuments { get; set; }
    DbSet<ChildFragment> ChildFragments { get; set; }
    DbSet<IngestionMetric> IngestionMetrics { get; set; }
    DbSet<EvaluationRun> EvaluationRuns { get; set; }
    DbSet<EvaluationPipelineResult> EvaluationPipelineResults { get; set; }

    /// <summary>
    /// Runs <paramref name="operation"/> in a database transaction under the provider's retrying execution
    /// strategy. The operation may run more than once, so it must start from a clean state on each attempt.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);

    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
