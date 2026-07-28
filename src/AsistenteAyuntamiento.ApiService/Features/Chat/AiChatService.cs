using AsistenteAyuntamiento.ApiService.Features.Chat;
using AsistenteAyuntamiento.ApiService.Features.AiConfig;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
#pragma warning disable SKEXP0001
using Microsoft.SemanticKernel.Embeddings;
#pragma warning restore SKEXP0001

/// <summary>
/// Result of an AI chat completion request.
/// </summary>
/// <param name="Success">Whether the completion succeeded.</param>
/// <param name="Content">The generated text content, or an error message on failure.</param>
/// <param name="DurationMs">Elapsed time in milliseconds.</param>
/// <param name="ErrorMessage">Error details when <paramref name="Success"/> is <c>false</c>; otherwise <c>null</c>.</param>
/// <param name="TokenUsage">Token consumption breakdown for this request.</param>
public sealed record DocumentSource(string Title, string Department, string Date, string BlobPath);

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
public sealed record TokenUsageInfo(int InputTokens, int OutputTokens, int TotalTokens)
{
    public static readonly TokenUsageInfo Empty = new(0, 0, 0);
}

/// <summary>
/// Wraps <see cref="IChatCompletionService"/> and integrates metrics and
/// distributed-tracing internally so callers don't need to manage observability.
/// </summary>
public sealed class AiChatService
{
    private readonly AiConfigurationService _aiConfigurationService;
    private readonly IConfiguration _configuration;
    private readonly AiMetricsService _metricsService;
    private readonly ILogger<AiChatService> _logger;
    private readonly AsistenteAyuntamiento.ApiService.Infrastructure.Data.AppDbContext _dbContext;
    private readonly Kernel _kernel;

    public AiChatService(
        AiConfigurationService aiConfigurationService,
        IConfiguration configuration,
        AiMetricsService metricsService,
        ILogger<AiChatService> logger,
        AsistenteAyuntamiento.ApiService.Infrastructure.Data.AppDbContext dbContext,
        Kernel kernel)
    {
        _aiConfigurationService = aiConfigurationService;
        _configuration = configuration;
        _metricsService = metricsService;
        _logger = logger;
        _dbContext = dbContext;
        _kernel = kernel;
    }

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

        var fullConfig = await _aiConfigurationService.GetFullConfigurationAsync();
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
            if (config.Provider == "openai")
            {
                kernelBuilder.AddOpenAIChatCompletion(modelId, apiKey ?? string.Empty);
            }
            else
            {
                var ollamaConnString = _configuration.GetConnectionString("ollama") ?? "http://localhost:11434";
                var ollamaEndpoint = ollamaConnString.StartsWith("Endpoint=") 
                    ? ollamaConnString.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length) 
                    : ollamaConnString;
                #pragma warning disable SKEXP0070
                kernelBuilder.AddOllamaChatCompletion(modelId, new Uri(ollamaEndpoint));
                #pragma warning restore SKEXP0070
            }
            var kernel = kernelBuilder.Build();
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            
            // --- RAG VECTOR SEARCH ---
            var documentSources = new List<DocumentSource>();
            if (!string.IsNullOrWhiteSpace(lastUserMessage?.Content))
            {
                #pragma warning disable CS0618
                var embeddingGenerator = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
                #pragma warning restore CS0618
                var queryEmbedding = await embeddingGenerator.GenerateEmbeddingAsync(lastUserMessage.Content, _kernel, cancellationToken);
                var queryVector = new Pgvector.Vector(queryEmbedding.ToArray());

                // Find top 3 closest chunks using CosineDistance
                var closestChunks = await _dbContext.DocumentChunks
                    .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
                    .Take(3)
                    .ToListAsync(cancellationToken);

                if (closestChunks.Any())
                {
                    var contextText = string.Join("\n\n---\n\n", closestChunks.Select(c => 
                        $"[Documento: {c.Title} | Departamento: {c.Department} | Fecha: {c.PublicationDate:yyyy-MM-dd}]\n{c.Content}"));
                    
                    var ragPrompt = $"Utiliza EXCLUSIVAMENTE el siguiente contexto legal extraído de los boletines oficiales para responder a la pregunta del usuario. Si el contexto no contiene la respuesta, indica que no tienes información suficiente basándote en los boletines publicados.\n\nContexto:\n{contextText}";
                    
                    // Insert context before the last user message
                    var lastMsgIndex = history.Count - 1;
                    history.Insert(lastMsgIndex, new ChatMessageContent(AuthorRole.System, ragPrompt));

                    documentSources = closestChunks.Select(c => new DocumentSource(
                        c.Title, 
                        c.Department, 
                        c.PublicationDate.ToString("yyyy-MM-dd"), 
                        GetPublicUrl(c.Source, c.DocumentId))).Distinct().ToList();
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

            var errorMessage = $"Error de comunicación con el modelo de IA local: {ex.Message}";

            return new AiCompletionResult(
                Success: false,
                Content: errorMessage,
                DurationMs: stopwatch.Elapsed.TotalMilliseconds,
                ErrorMessage: errorMessage,
                TokenUsage: TokenUsageInfo.Empty);
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
}
