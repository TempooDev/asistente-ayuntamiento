using System.Diagnostics;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Arena.Models;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.Arena;
using AsistenteAyuntamiento.Application.Common;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.Extensions.AI;

using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.Application.Features.Arena;

public class ArenaService(
    IAppDbContext dbContext,
    IServiceScopeFactory serviceScopeFactory,
    Kernel kernel,
    ILogger<ArenaService> logger) : IArenaService
{
    private readonly IChatCompletionService _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    public async Task<ArenaCompareResponse> CompareAsync(ArenaCompareRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();

        // Start baseline pipeline in the current scope
        var baselineTask = RunBaselinePipelineAsync(request.Query, cancellationToken);

        // Start hierarchical pipeline in a new DI scope to avoid DbContext concurrency
        var hierarchicalTask = Task.Run(async () =>
        {
            using var scope = serviceScopeFactory.CreateScope();
            var scopedExpansion = scope.ServiceProvider.GetRequiredService<IQueryExpansionService>();
            var scopedRetrieval = scope.ServiceProvider.GetRequiredService<IHybridRetrievalService>();
            var scopedGeneration = scope.ServiceProvider.GetRequiredService<IClearLanguageGenerationService>();
            
            return await RunHierarchicalPipelineScopedAsync(request.Query, scopedExpansion, scopedRetrieval, scopedGeneration, cancellationToken);
        }, cancellationToken);

        await Task.WhenAll(baselineTask, hierarchicalTask);
        var baselineResult = baselineTask.Result;
        var hierarchicalResult = hierarchicalTask.Result;

        var isHierarchicalAlfa = Random.Shared.Next(2) == 0;

        var alfaResult = isHierarchicalAlfa ? hierarchicalResult : baselineResult;
        var betaResult = isHierarchicalAlfa ? baselineResult : hierarchicalResult;

        var battle = new ArenaBattle
        {
            SessionId = sessionId,
            UserQuery = request.Query,
            CreatedAt = DateTime.UtcNow,
            LeftSystem = isHierarchicalAlfa ? PipelineType.Hierarchical : PipelineType.Baseline,
            RightSystem = isHierarchicalAlfa ? PipelineType.Baseline : PipelineType.Hierarchical,
            LeftResponse = alfaResult.Response,
            RightResponse = betaResult.Response,
            LeftLatencyMs = (int)(isHierarchicalAlfa ? alfaResult.Latency : betaResult.Latency),
            RightLatencyMs = (int)(isHierarchicalAlfa ? betaResult.Latency : alfaResult.Latency),
            LeftTokens = isHierarchicalAlfa ? alfaResult.Tokens : betaResult.Tokens,
            RightTokens = isHierarchicalAlfa ? betaResult.Tokens : alfaResult.Tokens,
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

    public async Task<ArenaVoteResponse> VoteAsync(ArenaVoteRequest request, CancellationToken cancellationToken = default)
    {
        
        var battle = await dbContext.ArenaBattles.FirstOrDefaultAsync(b => b.SessionId == request.SessionId, cancellationToken);
        if (battle == null)
            throw new Exception("Battle session not found");

        if (!Enum.TryParse<BattleWinner>(request.Winner, true, out var w))
            throw new ArgumentException("Invalid winner value provided.", nameof(request.Winner));

        battle.Winner = w;
        if (Enum.TryParse<EvaluationPreference>(request.ClarityReason, true, out var c)) battle.ClarityReason = c;
        if (Enum.TryParse<EvaluationPreference>(request.PrecisionReason, true, out var p)) battle.PrecisionReason = p;
        battle.OptionalComment = request.OptionalComment;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ArenaVoteResponse
        {
            AlfaSystem = battle.LeftSystem.ToString(),
            BetaSystem = battle.RightSystem.ToString()
        };
    }

    private async Task<(string Response, long Latency, string[] Sources, int Tokens)> RunBaselinePipelineAsync(string query, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var embeddings = await _embeddingService.GenerateAsync(new List<string> { query }, cancellationToken: cancellationToken);
            var queryVector = new Pgvector.Vector(embeddings[0].Vector.ToArray());

            
            var topChunks = await dbContext.DocumentChunks
                .AsNoTracking()
                .OrderBy(x => x.Embedding!.CosineDistance(queryVector))
                .Take(5)
                .ToListAsync(cancellationToken);

            var sources = topChunks.Select(c => c.Content).ToArray();
            var contextText = string.Join("\n\n", sources);
            var prompt = $@"Eres un asistente del Ayuntamiento. Responde a la consulta basándote únicamente en los siguientes documentos.
Consulta: {query}
Documentos:
{contextText}";

            var result = await _chatCompletionService.GetChatMessageContentAsync(prompt, cancellationToken: cancellationToken);
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
        IQueryExpansionService scopedExpansion,
        IHybridRetrievalService scopedRetrieval,
        IClearLanguageGenerationService scopedGeneration,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var expandedQuery = await scopedExpansion.ExpandQueryAsync(query, cancellationToken);
            var retrievalResults = await scopedRetrieval.RetrieveAsync(expandedQuery, 5, cancellationToken);
            var sources = retrievalResults.Select(r => r.ChunkText).ToArray();
            var response = await scopedGeneration.GenerateResponseAsync(query, retrievalResults, cancellationToken);
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


