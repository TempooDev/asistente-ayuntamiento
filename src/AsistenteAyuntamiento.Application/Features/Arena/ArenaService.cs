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

namespace AsistenteAyuntamiento.Application.Features.Arena;

public class ArenaService(
    IAppDbContext dbContext,
    IQueryExpansionService expansionService,
    IHybridRetrievalService retrievalService,
    IClearLanguageGenerationService generationService,
    Kernel kernel,
    ILogger<ArenaService> _logger) : IArenaService
{
    private readonly IChatCompletionService _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    public async Task<ArenaCompareResponse> CompareAsync(ArenaCompareRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();

        // Start both pipelines concurrently
        var baselineTask = RunBaselinePipelineAsync(request.Query, cancellationToken);
        var hierarchicalTask = RunHierarchicalPipelineAsync(request.Query, cancellationToken);

        await Task.WhenAll(baselineTask, hierarchicalTask);

        var isHierarchicalAlfa = new Random().Next(2) == 0;

        var alfaResult = isHierarchicalAlfa ? hierarchicalTask.Result : baselineTask.Result;
        var betaResult = isHierarchicalAlfa ? baselineTask.Result : hierarchicalTask.Result;

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

        if (Enum.TryParse<BattleWinner>(request.Winner, true, out var w)) battle.Winner = w;
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

    private async Task<(string Response, long Latency, string[] Sources)> RunBaselinePipelineAsync(string query, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var embeddings = await _embeddingService.GenerateAsync(new List<string> { query }, cancellationToken: cancellationToken);
            var queryVector = new Pgvector.Vector(embeddings[0].Vector.ToArray());

            
            var topChunks = await dbContext.DocumentChunks
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

            return (result.Content ?? "Error baseline", sw.ElapsedMilliseconds, sources);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Baseline pipeline");
            sw.Stop();
            return ("Error en el procesamiento estándar.", sw.ElapsedMilliseconds, []);
        }
    }

    private async Task<(string Response, long Latency, string[] Sources)> RunHierarchicalPipelineAsync(string query, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var expandedQuery = await expansionService.ExpandQueryAsync(query, cancellationToken);
            var retrievalResults = await retrievalService.RetrieveAsync(expandedQuery, 5, cancellationToken);
            var sources = retrievalResults.Select(r => r.ChunkText).ToArray();
            var response = await generationService.GenerateResponseAsync(query, retrievalResults, cancellationToken);
            sw.Stop();
            return (response, sw.ElapsedMilliseconds, sources);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Hierarchical pipeline");
            sw.Stop();
            return ("Error en el procesamiento jerárquico.", sw.ElapsedMilliseconds, []);
        }
    }
}


