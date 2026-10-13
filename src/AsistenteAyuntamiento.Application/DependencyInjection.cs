using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Application.Features.Metrics;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Application.Features.Scraper;
using AsistenteAyuntamiento.Application.Features.Users;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers every Application service. Hosts resolve only what they use: the API serves chat, Arena and
    /// administration; the Worker runs one ingestion pipeline. Ports are implemented by <c>AddInfrastructureServices</c>.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        // Ingestion
        services.AddSingleton<IChunkingStrategy>(new FixedOverlapChunkingStrategy(
            configuration.GetValue("Ai:Embeddings:ChunkMaxTokens", 400),
            configuration.GetValue("Ai:Embeddings:ChunkOverlapTokens", 50)));
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
        services.AddScoped<IIngestionAdminService, IngestionAdminService>();
        services.AddScoped<IFragmentEnrichmentService, FragmentEnrichmentService>();
        services.AddScoped<IIngestionMetricsService, IngestionMetricsService>();
        services.AddKeyedScoped<IHierarchicalIngestionProcessor, BoeIngestionService>(BulletinType.BOE.ToString());
        services.AddKeyedScoped<IHierarchicalIngestionProcessor, BojaIngestionService>(BulletinType.BOJA.ToString());

        // AI configuration and chat
        services.AddSingleton<IAiMetricsService, AiMetricsService>();
        services.AddScoped<IAiConfigurationService, AiConfigurationService>();
        services.AddScoped<IChatSessionService, ChatSessionService>();
        services.AddSingleton<ChatMessageBuffer>();
        services.AddScoped<IAiCallRecorder, AiCallRecorder>();
        services.AddScoped<IChatStreamingService, ChatStreamingService>();
        services.AddScoped<BaselineRagPipeline>();
        services.AddScoped<IRagPipeline>(sp => sp.GetRequiredService<BaselineRagPipeline>());
        services.AddScoped<IRagPipeline, HierarchicalRagPipeline>();
        services.AddScoped<IWinningPipelineSelector, WinningPipelineSelector>();
        services.AddSingleton<IShadowTestScheduler, ShadowTestScheduler>();
        services.AddScoped<IArenaStreamingService, ArenaStreamingService>();
        services.AddScoped<IAiChatService, AiChatService>();

        // Retrieval, generation and Arena
        services.AddScoped<IQueryExpansionService, QueryExpansionService>();
        services.AddScoped<IHybridRetrievalService, HybridRetrievalService>();
        services.AddScoped<IClearLanguageGenerationService, ClearLanguageGenerationService>();
        services.AddScoped<IArenaService, ArenaService>();
        services.AddScoped<IArenaAnalyticsService, ArenaAnalyticsService>();
        services.AddScoped<IReadabilityService, ReadabilityService>();

        // Users and scraper administration
        services.AddScoped<IUserPreferenceService, UserPreferenceService>();
        services.AddScoped<IHistoryAnalyzerService, HistoryAnalyzerService>();
        services.AddSingleton<ScraperStateService>();
        services.AddScoped<IScraperFilterService, ScraperFilterService>();

        return services;
    }
}
