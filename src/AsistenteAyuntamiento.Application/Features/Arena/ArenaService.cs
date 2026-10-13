using System.Diagnostics;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Arena.Models;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Arena;

public class ArenaService(
    IAppDbContext dbContext,
    IServiceScopeFactory serviceScopeFactory,
    Kernel kernel,
    IVectorStore vectorStore,
    IConfiguration configuration,
    ILlmUsageMetrics usageMetrics,
    ILogger<ArenaService> logger) : IArenaService
{
    private readonly IChatCompletionService _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
    private readonly (string Provider, string Model) _model = PlatformModels.Chat(configuration);
    private readonly IVectorStore _vectorStore = vectorStore;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    public async Task<ArenaCompareResponse> CompareAsync(ArenaCompareRequest request, string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();

        // Start baseline pipeline in the current scope
        var baselineTask = RunBaselinePipelineAsync(request.Query, new LlmCallScope(tenantId, LlmPipelines.Baseline), cancellationToken);

        // Start hierarchical pipeline in a new DI scope to avoid DbContext concurrency
        var hierarchicalTask = RunHierarchicalInOwnScopeAsync();

        async Task<(string Response, long Latency, string[] Sources, int Tokens)> RunHierarchicalInOwnScopeAsync()
        {
            await Task.Yield();
            using var scope = serviceScopeFactory.CreateScope();
            var scopedExpansion = scope.ServiceProvider.GetRequiredService<IQueryExpansionService>();
            var scopedRetrieval = scope.ServiceProvider.GetRequiredService<IHybridRetrievalService>();
            var scopedGeneration = scope.ServiceProvider.GetRequiredService<IClearLanguageGenerationService>();

            return await RunHierarchicalPipelineScopedAsync(request.Query, new LlmCallScope(tenantId, LlmPipelines.Hierarchical), scopedExpansion, scopedRetrieval, scopedGeneration, cancellationToken);
        }

        await Task.WhenAll(baselineTask, hierarchicalTask);
        var baselineResult = baselineTask.Result;
        var hierarchicalResult = hierarchicalTask.Result;

        var isHierarchicalAlfa = Random.Shared.Next(2) == 0;

        var alfaResult = isHierarchicalAlfa ? hierarchicalResult : baselineResult;
        var betaResult = isHierarchicalAlfa ? baselineResult : hierarchicalResult;

        var battle = new ArenaBattle
        {
            SessionId = sessionId,
            TenantId = tenantId,
            UserId = userId,
            UserQuery = request.Query,
            CreatedAt = DateTime.UtcNow,
            LeftSystem = isHierarchicalAlfa ? PipelineType.Hierarchical : PipelineType.Baseline,
            RightSystem = isHierarchicalAlfa ? PipelineType.Baseline : PipelineType.Hierarchical,
            LeftResponse = alfaResult.Response,
            RightResponse = betaResult.Response,
            // Left is always Alfa and right is always Beta.
            LeftLatencyMs = (int)alfaResult.Latency,
            RightLatencyMs = (int)betaResult.Latency,
            LeftTokens = alfaResult.Tokens,
            RightTokens = betaResult.Tokens,
            Winner = BattleWinner.Pending
        };

        dbContext.ArenaBattles.Add(battle);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ArenaCompareResponse
        {
            SessionId = sessionId,
            OptionAlfa = alfaResult.Response,
            OptionBeta = betaResult.Response,
            LatencyAlfaMs = alfaResult.Latency,
            LatencyBetaMs = betaResult.Latency,
            SourcesAlfa = alfaResult.Sources,
            SourcesBeta = betaResult.Sources
        };
    }

    public async Task<ArenaVoteResponse?> VoteAsync(ArenaVoteRequest request, string userId, CancellationToken cancellationToken = default)
    {
        // The Arena page sends ALFA / BETA / TIE / BOTH_BAD: case and underscores are ignored.
        if (!Enum.TryParse<BattleWinner>(request.Winner?.Replace("_", string.Empty), true, out var w) || !Enum.IsDefined(w) || w == BattleWinner.Pending)
            throw new ArgumentException("Invalid winner value provided.", nameof(request.Winner));

        // The tenant query filter scopes the lookup to the current tenant; the UserId predicate to the owner.
        var battle = await dbContext.ArenaBattles
            .FirstOrDefaultAsync(b => b.SessionId == request.SessionId && b.UserId == userId, cancellationToken);
        if (battle == null)
            return null;

        battle.Winner = w;
        if (Enum.TryParse<EvaluationPreference>(request.ClarityReason, true, out var c)) battle.ClarityReason = c;
        if (Enum.TryParse<EvaluationPreference>(request.PrecisionReason, true, out var p)) battle.PrecisionReason = p;
        battle.OptionalComment = request.OptionalComment;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ArenaVoteResponse
        {
            AlfaSystem = battle.LeftSystem.ToString(),
            BetaSystem = battle.RightSystem.ToString(),
            WinningResponse = w switch
            {
                BattleWinner.Alfa => battle.LeftResponse,
                BattleWinner.Beta => battle.RightResponse,
                _ => null
            }
        };
    }

    private async Task<(string Response, long Latency, string[] Sources, int Tokens)> RunBaselinePipelineAsync(string query, LlmCallScope llmScope, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var embeddings = await _embeddingService.GenerateAsync(new List<string> { query }, cancellationToken: cancellationToken);
            var queryVector = embeddings[0].Vector.ToArray();

            var searchResult = await _vectorStore.SearchAsync(
                VectorCollection.DocumentChunks,
                new VectorSearchRequest(queryVector, Limit: 5),
                cancellationToken);

            var sources = searchResult.Select(p => p.GetString(VectorPayloadKeys.Content)).ToArray();
            var contextText = string.Join("\n\n", sources);
            var prompt = string.Format(PromptCatalog.ArenaBaselineTemplate, query, contextText);

            var result = await _chatCompletionService.CompleteMeteredAsync(prompt, _model, llmScope, usageMetrics, cancellationToken);
            sw.Stop();

            int estimatedTokens = (prompt.Length + (result.Content?.Length ?? 0)) / 4;
            return (result.Content ?? "Error baseline", sw.ElapsedMilliseconds, sources, estimatedTokens);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in Baseline pipeline");
            sw.Stop();
            return ("Error en el procesamiento estándar.", sw.ElapsedMilliseconds, [], 0);
        }
    }

    private async Task<(string Response, long Latency, string[] Sources, int Tokens)> RunHierarchicalPipelineScopedAsync(
        string query,
        LlmCallScope llmScope,
        IQueryExpansionService scopedExpansion,
        IHybridRetrievalService scopedRetrieval,
        IClearLanguageGenerationService scopedGeneration,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var expandedQuery = await scopedExpansion.ExpandQueryAsync(query, llmScope, cancellationToken);
            var retrievalResults = await scopedRetrieval.RetrieveAsync(expandedQuery, 5, cancellationToken);
            var sources = retrievalResults.Select(r => r.ChunkText).ToArray();
            var response = await scopedGeneration.GenerateResponseAsync(query, retrievalResults, llmScope, cancellationToken);
            sw.Stop();

            // Rough estimation for hierarchical (expanded query, retrieval, generation)
            var contextText = string.Join("\n", retrievalResults.Select(r => r.ParentFullText));
            int estimatedTokens = (query.Length + contextText.Length + response.Length) / 4;

            return (response, sw.ElapsedMilliseconds, sources, estimatedTokens);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in Hierarchical pipeline");
            sw.Stop();
            return ("Error en el procesamiento jerárquico.", sw.ElapsedMilliseconds, [], 0);
        }
    }
}


