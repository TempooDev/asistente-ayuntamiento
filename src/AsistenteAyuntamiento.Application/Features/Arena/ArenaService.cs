using System.Diagnostics;
using AsistenteAyuntamiento.Application.Common.Interfaces;
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
using Microsoft.SemanticKernel.Embeddings;

namespace AsistenteAyuntamiento.Application.Features.Arena;

public class ArenaCompareRequest
{
    public string Query { get; set; } = string.Empty;
}

public class ArenaCompareResponse
{
    public Guid SessionId { get; set; }
    public string OptionAlfa { get; set; } = string.Empty;
    public string OptionBeta { get; set; } = string.Empty;
    public long LatencyAlfaMs { get; set; }
    public long LatencyBetaMs { get; set; }
}

public class ArenaVoteRequest
{
    public Guid SessionId { get; set; }
    public string Winner { get; set; } = string.Empty; // "ALFA", "BETA", or "TIE"
    public string? ClarityReason { get; set; }
    public string? PrecisionReason { get; set; }
    public string? OptionalComment { get; set; }
}

public interface IArenaService
{
    Task<ArenaCompareResponse> CompareAsync(ArenaCompareRequest request, CancellationToken cancellationToken = default);
    Task VoteAsync(ArenaVoteRequest request, CancellationToken cancellationToken = default);
}

public class ArenaService : IArenaService
{
    private readonly IAppDbContext _dbContext;
    private readonly IQueryExpansionService _expansionService;
    private readonly IHybridRetrievalService _retrievalService;
    private readonly IClearLanguageGenerationService _generationService;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ILogger<ArenaService> _logger;

#pragma warning disable SKEXP0001
    private readonly ITextEmbeddingGenerationService _embeddingService;
#pragma warning restore SKEXP0001

    public ArenaService(
        IAppDbContext dbContext,
        IQueryExpansionService expansionService,
        IHybridRetrievalService retrievalService,
        IClearLanguageGenerationService generationService,
        Kernel kernel,
        ILogger<ArenaService> logger)
    {
        _dbContext = dbContext;
        _expansionService = expansionService;
        _retrievalService = retrievalService;
        _generationService = generationService;
        _logger = logger;
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
#pragma warning disable SKEXP0001
        _embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
#pragma warning restore SKEXP0001
    }

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
            LeftSystem = isHierarchicalAlfa ? PipelineModes.HIERARCHICAL : PipelineModes.BASELINE,
            RightSystem = isHierarchicalAlfa ? PipelineModes.BASELINE : PipelineModes.HIERARCHICAL,
            LeftResponse = alfaResult.Response,
            RightResponse = betaResult.Response,
            LeftLatencyMs = (int)(isHierarchicalAlfa ? alfaResult.Latency : betaResult.Latency),
            RightLatencyMs = (int)(isHierarchicalAlfa ? betaResult.Latency : alfaResult.Latency),
            Winner = "PENDING"
        };

        _dbContext.ArenaBattles.Add(battle);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ArenaCompareResponse
        {
            SessionId = sessionId,
            OptionAlfa = alfaResult.Response,
            OptionBeta = betaResult.Response,
            LatencyAlfaMs = alfaResult.Latency,
            LatencyBetaMs = betaResult.Latency
        };
    }

    public async Task VoteAsync(ArenaVoteRequest request, CancellationToken cancellationToken = default)
    {
        var db = _dbContext as DbContext;
        var battle = await db.Set<ArenaBattle>().FirstOrDefaultAsync(b => b.SessionId == request.SessionId, cancellationToken);
        if (battle == null)
            throw new Exception("Battle session not found");

        battle.Winner = request.Winner;
        battle.ClarityReason = request.ClarityReason;
        battle.PrecisionReason = request.PrecisionReason;
        battle.OptionalComment = request.OptionalComment;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string Response, long Latency)> RunBaselinePipelineAsync(string query, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
#pragma warning disable SKEXP0001
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(new List<string> { query }, cancellationToken: cancellationToken);
            var queryVector = new Pgvector.Vector(embeddings.First().ToArray());
#pragma warning restore SKEXP0001

            var db = _dbContext as DbContext;
            var topChunks = await db.Set<DocumentChunk>()
                .OrderBy(x => x.Embedding.CosineDistance(queryVector))
                .Take(5)
                .ToListAsync(cancellationToken);

            var contextText = string.Join("\n\n", topChunks.Select(c => c.Content));
            var prompt = $@"Eres un asistente del Ayuntamiento. Responde a la consulta basándote únicamente en los siguientes documentos.
Consulta: {query}
Documentos:
{contextText}";

            var result = await _chatCompletionService.GetChatMessageContentAsync(prompt, cancellationToken: cancellationToken);
            sw.Stop();
            
            return (result.Content ?? "Error baseline", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Baseline pipeline");
            sw.Stop();
            return ("Error en el procesamiento estándar.", sw.ElapsedMilliseconds);
        }
    }

    private async Task<(string Response, long Latency)> RunHierarchicalPipelineAsync(string query, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var expandedQuery = await _expansionService.ExpandQueryAsync(query, cancellationToken);
            var retrievalResults = await _retrievalService.RetrieveAsync(expandedQuery, 5, cancellationToken);
            var response = await _generationService.GenerateResponseAsync(query, retrievalResults, cancellationToken);
            sw.Stop();
            return (response, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Hierarchical pipeline");
            sw.Stop();
            return ("Error en el procesamiento jerárquico.", sw.ElapsedMilliseconds);
        }
    }
}
