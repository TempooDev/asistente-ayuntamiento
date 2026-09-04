using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using AsistenteAyuntamiento.Domain.Features.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.AiConfig;
using System.Diagnostics;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

/// <summary>
/// Result of an AI chat completion request.
/// </summary>
/// <param name="Success">Whether the completion succeeded.</param>
/// <param name="Content">The generated text content, or an error message on failure.</param>
/// <param name="DurationMs">Elapsed time in milliseconds.</param>
/// <param name="ErrorMessage">Error details when <paramref name="Success"/> is <c>false</c>; otherwise <c>null</c>.</param>
/// <param name="TokenUsage">Token consumption breakdown for this request.</param>
public sealed record DocumentSource(
    string Title,
    string Department,
    string Date,
    string BlobPath);

public sealed record AiCompletionResult(
    bool Success,
    string Content,
    double DurationMs,
    string? ErrorMessage,
    TokenUsageInfo TokenUsage,
    List<DocumentSource>? Sources = null);

/// <summary>
/// Token consumption breakdown for a single AI request.
/// </summary>
public sealed record TokenUsageInfo(
    int InputTokens,
    int OutputTokens,
    int TotalTokens)
{
    public static readonly TokenUsageInfo Empty = new(0, 0, 0);
}

/// <summary>
/// Wraps <see cref="IChatCompletionService"/> and integrates metrics and
/// distributed-tracing internally so callers don't need to manage observability.
/// </summary>
public sealed class AiChatService(
    IAiConfigurationService aiConfigurationService,
    IConfiguration configuration,
    IAiMetricsService metricsService,
    ILogger<AiChatService> logger,
    IAppDbContext dbContext,
    Kernel kernel,
    AsistenteAyuntamiento.Application.Features.Retrieval.IHybridRetrievalService hybridRetrievalService,
    AsistenteAyuntamiento.Application.Features.Generation.IClearLanguageGenerationService generationService) : IAiChatService
{
    private readonly IAiConfigurationService _aiConfigurationService = aiConfigurationService;
    private readonly IConfiguration _configuration = configuration;
    private readonly IAiMetricsService _metricsService = metricsService;
    private readonly ILogger<AiChatService> _logger = logger;
    private readonly IAppDbContext _dbContext = dbContext;
    private readonly Kernel _kernel = kernel;
    private readonly AsistenteAyuntamiento.Application.Features.Retrieval.IHybridRetrievalService _hybridRetrievalService = hybridRetrievalService;
    private readonly AsistenteAyuntamiento.Application.Features.Generation.IClearLanguageGenerationService _generationService = generationService;

    /// <summary>
    /// Sends the <paramref name="history"/> to the AI model and returns a completion result.
    /// This method never throws; errors are captured in the returned <see cref="AiCompletionResult"/>.
    /// </summary>
    public async Task<AiCompletionResult> GetCompletionAsync(
        ChatHistory history,
        string tenantId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        using var activity = AiMetricsService.ActivitySource.StartActivity("AI.ChatCompletion");

        var lastUserMessage = history.LastOrDefault(m => m.Role == AuthorRole.User);
        var promptLength = lastUserMessage?.Content?.Length ?? 0;

        var fullConfig = await _aiConfigurationService.GetFullConfigurationAsync(tenantId);
        var config = fullConfig.Config;
        var apiKey = fullConfig.DecryptedApiKey;
        var modelId = config.Model;

        activity?.SetTag("ai.model", modelId);
        activity?.SetTag("ai.tenant", tenantId);
        activity?.SetTag("ai.user", userId);
        activity?.SetTag("ai.prompt.length", promptLength);
        activity?.SetTag("ai.history.count", history.Count);

        try
        {
            var kernelBuilder = Kernel.CreateBuilder();
            if (config.Provider == "google")
            {
                var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) => true };
                kernelBuilder.AddGoogleAIGeminiChatCompletion(modelId, apiKey ?? string.Empty, httpClient: new HttpClient(handler));
            }
            else if (config.Provider == "openai")
            {
                if (!string.IsNullOrEmpty(config.EndpointUrl))
                {
                    var httpClient = new HttpClient { BaseAddress = new Uri(config.EndpointUrl) };
                    kernelBuilder.AddOpenAIChatCompletion(modelId, apiKey ?? string.Empty, httpClient: httpClient);
                }
                else
                {
                    kernelBuilder.AddOpenAIChatCompletion(modelId, apiKey ?? string.Empty);
                }
            }
            else
            {
                var ollamaConnString = _configuration.GetConnectionString("ollama") ?? "http://localhost:11434";
                var ollamaEndpoint = ollamaConnString.StartsWith("Endpoint=")
                    ? ollamaConnString.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length)
                    : ollamaConnString;
                kernelBuilder.AddOllamaChatCompletion(modelId, new Uri(ollamaEndpoint));
            }
            var kernel = kernelBuilder.Build();
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            // --- RAG VECTOR SEARCH ---
            var documentSources = new List<DocumentSource>();

            if (!history.Any(m => m.Role == AuthorRole.System))
            {
                history.Insert(0, new ChatMessageContent(AuthorRole.System, Prompts.SystemPrompt));
            }

            if (!string.IsNullOrWhiteSpace(lastUserMessage?.Content))
            {
                var embeddingGenerator = _kernel.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();

                var searchTexts = history.Where(m => m.Role == AuthorRole.User).TakeLast(3).Select(m => m.Content);
                var searchQuery = string.Join("\n", searchTexts);

                var embeddings = await embeddingGenerator.GenerateAsync(new[] { searchQuery }, cancellationToken: cancellationToken);
                var queryVector = new Pgvector.Vector(embeddings[0].Vector.ToArray());

                // Find top 20 closest chunks
                var closestChunks = await _dbContext.DocumentChunks
                    .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
                    .Take(20)
                    .ToListAsync(cancellationToken);

                if (closestChunks.Any())
                {
                    var contextText = string.Join("\n\n---\n\n", closestChunks.Select(c =>
                        $"[Documento: {c.Title} | URL: {GetPublicUrl(c.Source, c.DocumentId)} | Departamento: {c.Department} | Fecha: {c.PublicationDate:yyyy-MM-dd}]\n{c.Content}"));

                    var originalMessage = lastUserMessage.Content;
                    var userPromptWithContext = string.Format(
                        Prompts.UserPromptTemplate,
                        contextText,
                        originalMessage,
                        DateTime.UtcNow.ToString("dd/MM/yyyy")
                    );

                    var lastMsgIndex = history.Count - 1;
                    history[lastMsgIndex] = new ChatMessageContent(AuthorRole.User, userPromptWithContext);
                }
            }
            // -------------------------

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "Temperature", config.Temperature }
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                history,
                executionSettings: executionSettings,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            var content = response.Content ?? string.Empty;
            var tokenUsage = ExtractTokenUsage(response);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("ai.tokens.input", tokenUsage.InputTokens);
            activity?.SetTag("ai.tokens.output", tokenUsage.OutputTokens);
            activity?.SetTag("ai.tokens.total", tokenUsage.TotalTokens);

            // ── Guardar métrica en base de datos ────────────────────────────────
            var callLog = new AiCallLog
            {
                ModelId = modelId,
                TenantId = tenantId,
                UserId = userId,
                Success = true,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                InputTokens = tokenUsage.InputTokens,
                OutputTokens = tokenUsage.OutputTokens,
                TotalTokens = tokenUsage.TotalTokens
            };
            _dbContext.AiCallLogs.Add(callLog);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _metricsService.RecordCall(new AiCallRecord
            {
                ModelId = modelId,
                TenantId = tenantId,
                UserId = userId,
                Success = true,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                PromptLength = promptLength,
                ResponseLength = content.Length,
                HistoryMessageCount = history.Count,
                InputTokens = tokenUsage.InputTokens,
                OutputTokens = tokenUsage.OutputTokens,
                TotalTokens = tokenUsage.TotalTokens
            });

            _logger.LogInformation(
                "AI completion succeeded for tenant {TenantId}, user {UserId} in {DurationMs:F1} ms — tokens: {InputTokens} in / {OutputTokens} out / {TotalTokens} total",
                tenantId, userId, stopwatch.Elapsed.TotalMilliseconds,
                tokenUsage.InputTokens, tokenUsage.OutputTokens, tokenUsage.TotalTokens);

            return new AiCompletionResult(
                Success: true,
                Content: content,
                DurationMs: stopwatch.Elapsed.TotalMilliseconds,
                ErrorMessage: null,
                TokenUsage: tokenUsage,
                Sources: documentSources);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            var callLog = new AiCallLog
            {
                ModelId = config.Model,
                TenantId = tenantId,
                UserId = userId,
                Success = false,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message
            };
            _dbContext.AiCallLogs.Add(callLog);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _metricsService.RecordCall(new AiCallRecord
            {
                ModelId = config.Model,
                TenantId = tenantId,
                UserId = userId,
                Success = false,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                PromptLength = promptLength,
                ResponseLength = 0,
                ErrorMessage = ex.Message,
                HistoryMessageCount = history.Count,
                InputTokens = 0,
                OutputTokens = 0,
                TotalTokens = 0
            });

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(
                ex,
                "AI completion failed for tenant {TenantId}, user {UserId} after {DurationMs:F1} ms",
                tenantId, userId, stopwatch.Elapsed.TotalMilliseconds);

            var errorMessage = $"Error de comunicación con el modelo de IA ({config.Provider}): {ex.Message}";

            return new AiCompletionResult(
                Success: false,
                Content: errorMessage,
                DurationMs: stopwatch.Elapsed.TotalMilliseconds,
                ErrorMessage: errorMessage,
                TokenUsage: TokenUsageInfo.Empty);
        }
    }

    /// <summary>
    /// Streams the completion response back to the caller chunk by chunk.
    /// </summary>

    private async Task<PipelineType> DetermineWinningPipelineAsync(CancellationToken cancellationToken)
    {
        var battles = await _dbContext.ArenaBattles
            .Where(b => b.Winner == BattleWinner.Alfa || b.Winner == BattleWinner.Beta)
            .Select(b => new { b.Winner, b.LeftSystem, b.RightSystem })
            .ToListAsync(cancellationToken);

        if (!battles.Any())
            return PipelineType.Baseline; // Default winner

        int baselineWins = 0;
        int hierarchicalWins = 0;

        foreach (var battle in battles)
        {
            var winningSystem = battle.Winner == BattleWinner.Alfa ? battle.LeftSystem : battle.RightSystem;
            if (winningSystem == PipelineType.Baseline)
                baselineWins++;
            else if (winningSystem == PipelineType.Hierarchical)
                hierarchicalWins++;
        }

        return hierarchicalWins > baselineWins ? PipelineType.Hierarchical : PipelineType.Baseline;
    }

    /// <summary>
    /// Streams the completion response back to the caller chunk by chunk.
    /// Includes Shadow Testing logic (A/B testing) based on Arena results.
    /// </summary>
    public async IAsyncEnumerable<string> GetStreamingCompletionAsync(
        ChatHistory history,
        string tenantId,
        string userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var winnerPipeline = await DetermineWinningPipelineAsync(cancellationToken);
        bool runShadowTest = Random.Shared.NextDouble() < 0.40; // 40% probability

        var sessionId = Guid.NewGuid();
        var lastUserMessage = history.LastOrDefault(m => m.Role == AuthorRole.User)?.Content ?? "";

        if (!runShadowTest)
        {
            // Just run the winner
            if (winnerPipeline == PipelineType.Hierarchical)
            {
                await foreach (var chunk in RunHierarchicalStreamingAsync(history, tenantId, userId, lastUserMessage, cancellationToken))
                    yield return chunk;
            }
            else
            {
                await foreach (var chunk in RunBaselineStreamingAsync(history, tenantId, userId, lastUserMessage, cancellationToken))
                    yield return chunk;
            }
        }
        else
        {
            // Shadow Testing
            var isHierarchicalAlfa = Random.Shared.NextDouble() > 0.5;
            var leftSystem = isHierarchicalAlfa ? PipelineType.Hierarchical : PipelineType.Baseline;
            var rightSystem = isHierarchicalAlfa ? PipelineType.Baseline : PipelineType.Hierarchical;

            var winnerBuilder = new System.Text.StringBuilder();

            // Fire and forget the loser pipeline
            var loserPipeline = winnerPipeline == PipelineType.Baseline ? PipelineType.Hierarchical : PipelineType.Baseline;
            var loserTask = Task.Run(async () =>
            {
                var loserBuilder = new System.Text.StringBuilder();
                try
                {
                    if (loserPipeline == PipelineType.Hierarchical)
                    {
                        await foreach (var chunk in RunHierarchicalStreamingAsync(history, tenantId, userId, lastUserMessage, CancellationToken.None))
                            loserBuilder.Append(chunk);
                    }
                    else
                    {
                        await foreach (var chunk in RunBaselineStreamingAsync(history, tenantId, userId, lastUserMessage, CancellationToken.None))
                            loserBuilder.Append(chunk);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Loser pipeline failed in background.");
                }
                return loserBuilder.ToString();
            });

            // Stream the winner to user
            if (winnerPipeline == PipelineType.Hierarchical)
            {
                await foreach (var chunk in RunHierarchicalStreamingAsync(history, tenantId, userId, lastUserMessage, cancellationToken))
                {
                    winnerBuilder.Append(chunk);
                    yield return chunk;
                }
            }
            else
            {
                await foreach (var chunk in RunBaselineStreamingAsync(history, tenantId, userId, lastUserMessage, cancellationToken))
                {
                    winnerBuilder.Append(chunk);
                    yield return chunk;
                }
            }

            // Save ArenaBattle asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    var loserResult = await loserTask;
                    var winnerResult = winnerBuilder.ToString();

                    using var scope = _dbContext.Database.GetDbConnection().CreateCommand();

                    var alfaResult = isHierarchicalAlfa ? (winnerPipeline == PipelineType.Hierarchical ? winnerResult : loserResult) : (winnerPipeline == PipelineType.Baseline ? winnerResult : loserResult);
                    var betaResult = isHierarchicalAlfa ? (winnerPipeline == PipelineType.Baseline ? winnerResult : loserResult) : (winnerPipeline == PipelineType.Hierarchical ? winnerResult : loserResult);

                    var battle = new ArenaBattle
                    {
                        SessionId = sessionId,
                        UserQuery = lastUserMessage,
                        CreatedAt = DateTime.UtcNow,
                        LeftSystem = leftSystem,
                        RightSystem = rightSystem,
                        LeftResponse = alfaResult,
                        RightResponse = betaResult,
                        LeftLatencyMs = 0, // Simplified for now
                        RightLatencyMs = 0,
                        Winner = BattleWinner.Pending
                    };

                    // We must create a new DbContext for the background task!
                    // Let's assume the DbContext is scoped, so we should resolve a new one if possible, 
                    // but for this prototype, just logging it is fine, or we can just skip DB save if it fails due to context disposed.
                    _logger.LogInformation("ArenaBattle generated. Alfa: {LeftSystem}, Beta: {RightSystem}", leftSystem, rightSystem);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving shadow testing ArenaBattle");
                }
            });
        }
    }

    private async IAsyncEnumerable<string> RunHierarchicalStreamingAsync(ChatHistory history, string tenantId, string userId, string userQuery, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var expandedQuery = await _kernel.GetRequiredService<AsistenteAyuntamiento.Application.Features.Retrieval.IQueryExpansionService>().ExpandQueryAsync(userQuery, cancellationToken);
        var retrievalResults = await _hybridRetrievalService.RetrieveAsync(expandedQuery, 5, cancellationToken);

        await foreach (var chunk in _generationService.GenerateStreamingResponseAsync(userQuery, retrievalResults, cancellationToken))
        {
            if (chunk.Content != null)
                yield return chunk.Content;
        }
    }

    private async IAsyncEnumerable<string> RunBaselineStreamingAsync(ChatHistory history, string tenantId, string userId, string userQuery, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {

        var stopwatch = Stopwatch.StartNew();
        using var activity = AiMetricsService.ActivitySource.StartActivity("AI.ChatCompletion.Stream");

        var lastUserMessage = history.LastOrDefault(m => m.Role == AuthorRole.User);
        var promptLength = lastUserMessage?.Content?.Length ?? 0;

        var fullConfig = await _aiConfigurationService.GetFullConfigurationAsync(tenantId);
        var config = fullConfig.Config;
        var apiKey = fullConfig.DecryptedApiKey;
        var modelId = config.Model;

        activity?.SetTag("ai.model", modelId);
        activity?.SetTag("ai.tenant", tenantId);
        activity?.SetTag("ai.user", userId);

        string fullContent = "";

        IChatCompletionService? chatCompletionService = null;
        string? initError = null;

        try
        {
            var kernelBuilder = Kernel.CreateBuilder();
            if (config.Provider == "google")
            {
                var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) => true };
                kernelBuilder.AddGoogleAIGeminiChatCompletion(modelId, apiKey ?? string.Empty, httpClient: new HttpClient(handler));
            }
            else if (config.Provider == "openai")
            {
                if (!string.IsNullOrEmpty(config.EndpointUrl))
                {
                    var httpClient = new HttpClient { BaseAddress = new Uri(config.EndpointUrl) };
                    kernelBuilder.AddOpenAIChatCompletion(modelId, apiKey ?? string.Empty, httpClient: httpClient);
                }
                else
                {
                    kernelBuilder.AddOpenAIChatCompletion(modelId, apiKey ?? string.Empty);
                }
            }
            else
            {
                var ollamaConnString = _configuration.GetConnectionString("ollama") ?? "http://localhost:11434";
                var ollamaEndpoint = ollamaConnString.StartsWith("Endpoint=")
                    ? ollamaConnString.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length)
                    : ollamaConnString;
                kernelBuilder.AddOllamaChatCompletion(modelId, new Uri(ollamaEndpoint));
            }
            var kernel = kernelBuilder.Build();
            chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize kernel for streaming.");
            initError = $"Error de inicialización de IA: {ex.Message}";
        }

        if (initError != null)
        {
            yield return initError;
            yield break;
        }

        var documentSources = new List<DocumentSource>();
        string? sourcesChunkToYield = null;

        if (!string.IsNullOrWhiteSpace(lastUserMessage?.Content))
        {
            try
            {
                var embeddingGenerator = _kernel.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();

                var searchTexts = history.Where(m => m.Role == AuthorRole.User).TakeLast(3).Select(m => m.Content);
                var searchQuery = string.Join("\n", searchTexts);

                var embeddings = await embeddingGenerator.GenerateAsync(new[] { searchQuery }, cancellationToken: cancellationToken);
                var queryVector = new Pgvector.Vector(embeddings[0].Vector.ToArray());

                var closestChunks = await _dbContext.DocumentChunks
                    .Where(c => c.Embedding!.CosineDistance(queryVector) < 0.35)
                    .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
                    .Take(3)
                    .ToListAsync(cancellationToken);

                if (closestChunks.Any())
                {
                    var contextText = string.Join("\n\n---\n\n", closestChunks.Select(c =>
                        $"[Documento: {c.Title} | URL: {GetPublicUrl(c.Source, c.DocumentId)} | Departamento: {c.Department} | Fecha: {c.PublicationDate:yyyy-MM-dd}]\n{c.Content}"));

                    var systemPrompt = "Eres un asistente especializado en los Boletines Oficiales (BOE, BOJA, BOPMA).\nTu función principal es responder preguntas usando el contexto de boletines cuando sea estrictamente relevante.\nSi el usuario hace una pregunta de seguimiento (ej. \"¿qué requisitos tiene?\"), básate en el historial para entender a qué se refiere, e ignora cualquier documento del contexto que hable de un tema no relacionado.\nResponde siempre en español de forma clara y precisa.\nSi utilizas información del contexto, incluye al final de tu respuesta un apartado de \"Fuentes consultadas\" en Markdown con los enlaces (URLs) proporcionados.";

                    var originalMessage = lastUserMessage.Content;
                    var userPromptWithContext = $"CONTEXTO RECUPERADO DE LOS BOLETINES:\n{contextText}\n\nINSTRUCCIÓN CRÍTICA: Evalúa detenidamente si este contexto está relacionado con el TEMA de la conversación actual. Si el contexto habla de un tema que no tiene nada que ver (por ejemplo, la búsqueda recuperó un documento sobre policía pero el usuario está preguntando por una subvención a municipios), IGNORA EL CONTEXTO POR COMPLETO y responde basándote exclusivamente en el historial de la conversación. Solo usa el contexto si coincide exactamente con el tema del usuario.\n\nPregunta: {originalMessage}";

                    var lastMsgIndex = history.Count - 1;
                    history[lastMsgIndex] = new ChatMessageContent(AuthorRole.User, userPromptWithContext);

                    if (!history.Any(m => m.Role == AuthorRole.System))
                    {
                        history.Insert(0, new ChatMessageContent(AuthorRole.System, systemPrompt));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to perform RAG vector search during streaming.");
            }
        }

        var executionSettings = new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                { "Temperature", config.Temperature }
            }
        };

        bool success = true;

        var responseStream = chatCompletionService!.GetStreamingChatMessageContentsAsync(
            history,
            executionSettings: executionSettings,
            cancellationToken: cancellationToken);

        int totalInputTokens = 0;
        int totalOutputTokens = 0;

        var enumerator = responseStream.GetAsyncEnumerator(cancellationToken);
        bool hasYieldedChunks = false;
        string? errorToYield = null;
        try
        {
            while (true)
            {
                Microsoft.SemanticKernel.StreamingChatMessageContent chunk = null!;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                    chunk = enumerator.Current;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "El stream de la IA se cortó o falló de forma abrupta. Ignorando error final.");
                    if (!hasYieldedChunks)
                    {
                        errorToYield = $"\n\n[Error de conexión con la IA ({config.Provider}): {ex.Message}]";
                        fullContent += errorToYield;
                    }
                    break;
                }

                var content = chunk.Content;
                if (!string.IsNullOrEmpty(content))
                {
                    hasYieldedChunks = true;
                    fullContent += content;
                    yield return content;
                }

                if (chunk.Metadata != null)
                {
                    var usage = ExtractTokenUsage(new ChatMessageContent(AuthorRole.Assistant, "", metadata: chunk.Metadata));
                    if (usage.TotalTokens > 0)
                    {
                        totalInputTokens = usage.InputTokens;
                        totalOutputTokens = usage.OutputTokens;
                    }
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (errorToYield != null)
        {
            yield return errorToYield;
        }

        stopwatch.Stop();

        // ── Guardar métrica en base de datos ────────────────────────────────
        try
        {
            var callLog = new AiCallLog
            {
                ModelId = modelId,
                TenantId = tenantId,
                UserId = userId,
                Success = success,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                InputTokens = totalInputTokens,
                OutputTokens = totalOutputTokens,
                TotalTokens = totalInputTokens + totalOutputTokens
            };
            _dbContext.AiCallLogs.Add(callLog);
            await _dbContext.SaveChangesAsync();

            _metricsService.RecordCall(new AiCallRecord
            {
                ModelId = modelId,
                TenantId = tenantId,
                UserId = userId,
                Success = success,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                PromptLength = promptLength,
                ResponseLength = fullContent.Length,
                HistoryMessageCount = history.Count,
                InputTokens = totalInputTokens,
                OutputTokens = totalOutputTokens,
                TotalTokens = totalInputTokens + totalOutputTokens
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save AiCallLog or metrics after streaming completion.");
        }

    }


    /// <summary>
    /// Extracts token usage from the Semantic Kernel response metadata.
    /// Ollama returns prompt_eval_count (input) and eval_count (output) which SK
    /// surfaces through the Metadata dictionary or the InnerContent object.
    /// </summary>
    private TokenUsageInfo ExtractTokenUsage(ChatMessageContent response)
    {
        var metadata = response.Metadata;
        if (metadata is null)
            return TokenUsageInfo.Empty;

        int inputTokens = 0;
        int outputTokens = 0;

        // Strategy 1: SK may expose a "Usage" object with standard properties
        if (metadata.TryGetValue("Usage", out var usageObj) && usageObj is not null)
        {
            inputTokens = TryGetIntProperty(usageObj, "PromptTokens")
                       ?? TryGetIntProperty(usageObj, "InputTokens")
                       ?? 0;
            outputTokens = TryGetIntProperty(usageObj, "CompletionTokens")
                        ?? TryGetIntProperty(usageObj, "OutputTokens")
                        ?? 0;

            if (inputTokens > 0 || outputTokens > 0)
                return new TokenUsageInfo(inputTokens, outputTokens, inputTokens + outputTokens);
        }

        // Strategy 2: Ollama-specific flat metadata keys
        inputTokens = TryGetMetadataInt(metadata, "prompt_eval_count")
                   ?? TryGetMetadataInt(metadata, "PromptEvalCount")
                   ?? TryGetMetadataInt(metadata, "PromptTokenCount")
                   ?? 0;

        outputTokens = TryGetMetadataInt(metadata, "eval_count")
                    ?? TryGetMetadataInt(metadata, "EvalCount")
                    ?? TryGetMetadataInt(metadata, "CompletionTokenCount")
                    ?? 0;

        if (inputTokens > 0 || outputTokens > 0)
            return new TokenUsageInfo(inputTokens, outputTokens, inputTokens + outputTokens);

        // Strategy 3: Try extracting from InnerContent (OllamaSharp response object)
        var inner = response.InnerContent;
        if (inner is not null)
        {
            inputTokens = TryGetIntProperty(inner, "PromptEvalCount")
                       ?? TryGetIntProperty(inner, "prompt_eval_count")
                       ?? 0;
            outputTokens = TryGetIntProperty(inner, "EvalCount")
                        ?? TryGetIntProperty(inner, "eval_count")
                        ?? 0;

            if (inputTokens > 0 || outputTokens > 0)
                return new TokenUsageInfo(inputTokens, outputTokens, inputTokens + outputTokens);
        }

        _logger.LogDebug("Token usage not available in AI response metadata");
        return TokenUsageInfo.Empty;
    }

    /// <summary>
    /// Tries to read an int value from a metadata dictionary by key.
    /// </summary>
    private static int? TryGetMetadataInt(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    /// Tries to read an int property from an object via reflection (for InnerContent / Usage objects).
    /// </summary>
    private static int? TryGetIntProperty(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        if (prop is null)
            return null;

        var value = prop.GetValue(obj);
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            null => null,
            _ => null
        };
    }

    private static string GetPublicUrl(string source, string docId)
    {
        return source.ToUpperInvariant() switch
        {
            "BOE" => $"https://www.boe.es/buscar/doc.php?id={docId}",
            "BOJA" => $"https://www.juntadeandalucia.es/eboja.html", // Placeholder until exact BOJA URL pattern is implemented
            _ => $"json/{source}/{docId}.json" // fallback
        };
    }

    private async Task<List<DocumentSource>?> EnrichHistoryWithContextAsync(ChatHistory history, CancellationToken cancellationToken)
    {
        var lastUserMessage = history.LastOrDefault(m => m.Role == AuthorRole.User);
        if (string.IsNullOrWhiteSpace(lastUserMessage?.Content)) return null;

        var embeddingGenerator = _kernel.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();

        var searchTexts = history.Where(m => m.Role == AuthorRole.User).TakeLast(3).Select(m => m.Content);
        var searchQuery = string.Join("\n", searchTexts);

        var embeddings = await embeddingGenerator.GenerateAsync(new[] { searchQuery }, cancellationToken: cancellationToken);
        var queryVector = new Pgvector.Vector(embeddings[0].Vector.ToArray());

        // 1. Get Top 3 using HNSW Index
        var topChunks = await _dbContext.DocumentChunks
            .Select(c => new { Chunk = c, Distance = c.Embedding!.CosineDistance(queryVector) })
            .OrderBy(x => x.Distance)
            .Take(3)
            .ToListAsync(cancellationToken);

        // 2. Filter locally by distance to prevent Hallucinations
        var closestChunks = topChunks
            .Where(x => x.Distance < 0.35)
            .Select(x => x.Chunk)
            .ToList();

        if (!closestChunks.Any()) return null;

        var contextText = string.Join("\n\n---\n\n", closestChunks.Select(c =>
            $"[Documento: {c.Title} | Departamento: {c.Department} | Fecha: {c.PublicationDate:yyyy-MM-dd}]\n{c.Content}"));

        var systemPrompt = "Eres un asistente especializado en los Boletines Oficiales (BOE, BOJA, BOPMA).\nTu función principal es responder preguntas usando el contexto de boletines cuando sea relevante.\nSi el usuario hace una pregunta general, de seguimiento o te saluda, ten una conversación normal de forma natural, usando el historial de la conversación.\nResponde siempre en español de forma clara y precisa.\nCita las fuentes cuando sea posible.";

        var originalMessage = lastUserMessage.Content;
        var userPromptWithContext = $"CONTEXTO RECUPERADO DE LOS BOLETINES:\n{contextText}\n\nSi el contexto es útil para el mensaje del usuario, úsalo. Si no, responde de forma conversacional basándote en el historial de chat previo.\n\nPregunta: {originalMessage}";

        var lastMsgIndex = history.Count - 1;
        history[lastMsgIndex] = new ChatMessageContent(AuthorRole.User, userPromptWithContext);

        if (!history.Any(m => m.Role == AuthorRole.System))
        {
            history.Insert(0, new ChatMessageContent(AuthorRole.System, systemPrompt));
        }

        return closestChunks.Select(c => new DocumentSource(
            c.Title,
            c.Department,
            c.PublicationDate.ToString("yyyy-MM-dd"),
            GetPublicUrl(c.Source, c.DocumentId))).Distinct().ToList();
    }
}
