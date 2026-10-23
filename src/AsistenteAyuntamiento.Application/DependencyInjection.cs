using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Evaluation;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Application.Features.Metrics;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Application.Features.Scraper;
using AsistenteAyuntamiento.Application.Features.Users;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

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
        AddChunkingStrategies(services, configuration);
        foreach (var variant in FlatChunkVariant.All)
        {
            services.AddKeyedScoped<IDocumentIngestionService>(variant.Pipeline, (sp, _) => ActivatorUtilities.CreateInstance<DocumentIngestionService>(
                sp, sp.GetRequiredKeyedService<IChunkingStrategy>(variant.ChunkingStrategy), variant));
        }
        // Unkeyed: the baseline (single-document processing from the API).
        services.AddScoped(sp => sp.GetRequiredKeyedService<IDocumentIngestionService>(PipelineType.Baseline));
        services.AddScoped<IIngestionAdminService, IngestionAdminService>();
        services.AddScoped<IFragmentEnrichmentService, FragmentEnrichmentService>();
        services.AddScoped<IIngestionMetricsService, IngestionMetricsService>();
        services.AddKeyedScoped<IHierarchicalIngestionProcessor, BoeIngestionService>(BulletinType.BOE.ToString());
        services.AddKeyedScoped<IHierarchicalIngestionProcessor, BojaIngestionService>(BulletinType.BOJA.ToString());

        // AI configuration and chat
        services.AddSingleton<IAiMetricsService, AiMetricsService>();
        services.AddSingleton<ILlmUsageMetrics, LlmUsageMetrics>();
        services.AddScoped<IAiConfigurationService, AiConfigurationService>();
        services.AddScoped<IChatSessionService, ChatSessionService>();
        services.AddSingleton<ChatMessageBuffer>();
        services.AddScoped<IAiCallRecorder, AiCallRecorder>();
        services.AddScoped<IChatStreamingService, ChatStreamingService>();
        // One flat-chunk pipeline per variant. The chat, Arena and shadow test only pick Baseline and Hierarchical;
        // the study variants answer the offline evaluation.
        foreach (var variant in FlatChunkVariant.All)
        {
            services.AddKeyedScoped(variant.Pipeline, (sp, _) => ActivatorUtilities.CreateInstance<FlatChunkRagPipeline>(sp, variant));
            services.AddScoped<IRagPipeline>(sp => sp.GetRequiredKeyedService<FlatChunkRagPipeline>(variant.Pipeline));
        }
        services.AddScoped<IRagPipeline, HierarchicalRagPipeline>();
        services.AddScoped<IWinningPipelineSelector, WinningPipelineSelector>();
        services.AddSingleton<IShadowTestScheduler, ShadowTestScheduler>();
        services.AddScoped<IArenaStreamingService, ArenaStreamingService>();
        services.AddScoped<IAiChatService, AiChatService>();

        // Retrieval, generation and Arena
        services.AddScoped<IQueryExpansionService, QueryExpansionService>();
        services.AddScoped<IHybridRetrievalService, HybridRetrievalService>();
        services.AddSingleton(new QueryEmbeddingOptions(
            configuration.GetValue("Ai:Embeddings:UseQueryInstruction", false) ? PromptCatalog.EmbeddingQueryInstruction : null));
        services.AddScoped<IClearLanguageGenerationService, ClearLanguageGenerationService>();
        services.AddScoped<IArenaService, ArenaService>();
        services.AddScoped<IArenaAnalyticsService, ArenaAnalyticsService>();
        services.AddScoped<IAiMetricsQueryService, AiMetricsQueryService>();
        services.AddScoped<IEvaluationResultsService, EvaluationResultsService>();

        // Users and scraper administration
        services.AddScoped<IUserPreferenceService, UserPreferenceService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IHistoryAnalyzerService, HistoryAnalyzerService>();
        services.AddSingleton<ScraperStateService>();
        services.AddScoped<IScraperFilterService, ScraperFilterService>();

        return services;
    }

    /// <summary>
    /// The three flat strategies, keyed by name; the unkeyed strategy is <c>FixedOverlap</c> (baseline and the
    /// hierarchical pipeline's article splitting). Invalid values throw here, so a misconfigured host fails at
    /// startup instead of on the first document.
    /// </summary>
    private static void AddChunkingStrategies(IServiceCollection services, IConfiguration configuration)
    {
        var maxTokens = configuration.GetValue("Ai:Embeddings:ChunkMaxTokens", FixedOverlapChunkingStrategy.DefaultMaxTokens);
        var overlapTokens = configuration.GetValue("Ai:Embeddings:ChunkOverlapTokens", FixedOverlapChunkingStrategy.DefaultOverlapTokens);

        var fixedOverlap = new FixedOverlapChunkingStrategy(
            maxTokens,
            overlapTokens,
            configuration.GetValue("Ai:Embeddings:ChunkMaxLines", FixedOverlapChunkingStrategy.DefaultMaxLineTokens));
        services.AddSingleton<IChunkingStrategy>(fixedOverlap);
        services.AddKeyedSingleton<IChunkingStrategy>(ChunkingStrategies.FixedOverlap, fixedOverlap);

        services.AddKeyedSingleton<IChunkingStrategy>(ChunkingStrategies.Recursive, new RecursiveChunkingStrategy(
            configuration.GetValue("Ai:Chunking:Recursive:ChunkMaxTokens", maxTokens),
            configuration.GetValue("Ai:Chunking:Recursive:ChunkOverlapTokens", overlapTokens)));

        var semanticMaxTokens = configuration.GetValue("Ai:Chunking:Semantic:ChunkMaxTokens", maxTokens);
        var semanticOptions = new SemanticChunkingOptions(
            configuration.GetValue("Ai:Chunking:Semantic:BufferSize", SemanticChunkingOptions.DefaultBufferSize),
            configuration.GetValue("Ai:Chunking:Semantic:BreakpointPercentile", SemanticChunkingOptions.DefaultBreakpointPercentile),
            // 100 with the default limit of 400; a quarter of a smaller limit, so lowering ChunkMaxTokens alone stays valid.
            configuration.GetValue("Ai:Chunking:Semantic:MinTokens", Math.Min(SemanticChunkingOptions.DefaultMinTokens, semanticMaxTokens / 4)),
            semanticMaxTokens);
        // The platform embedding model (never a tenant's), the same one that embeds the chunks.
        services.AddKeyedSingleton<IChunkingStrategy>(ChunkingStrategies.Semantic, (sp, _) => new SemanticChunkingStrategy(
            sp.GetRequiredService<Kernel>().GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(), semanticOptions));
    }
}
