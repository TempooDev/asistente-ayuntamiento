using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Domain.Features.Chat;

namespace AsistenteAyuntamiento.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<UserProfile> UserProfiles { get; set; }
    DbSet<ChatSession> ChatSessions { get; set; }
    DbSet<ChatMessage> ChatMessages { get; set; }
    DbSet<AiCallLog> AiCallLogs { get; set; }
    DbSet<AiConfiguration> AiConfigurations { get; set; }
    DbSet<DocumentChunk> DocumentChunks { get; set; }
    DbSet<DocumentJobState> DocumentJobStates { get; set; }
    DbSet<ScraperFilterRule> ScraperFilterRules { get; set; }
    
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
