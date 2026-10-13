using System.Globalization;
using System.Runtime.CompilerServices;
using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Users;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat.Pipelines;

/// <summary>
/// Baseline RAG: dense search over <see cref="VectorCollection.DocumentChunks"/>, context injected into the
/// last user message, answer from the tenant's chat model (with fallback) via <see cref="IChatStreamingService"/>.
/// </summary>
public sealed class BaselineRagPipeline(
    Kernel kernel,
    IVectorStore vectorStore,
    IUserPreferenceService userPreferenceService,
    IChatStreamingService chatStreamingService,
    ILogger<BaselineRagPipeline> logger) : IRagPipeline
{
    /// <summary>Messages (latest first) whose text is embedded for the search, so follow-up questions keep their topic.</summary>
    private const int SearchHistoryDepth = 3;

    private static readonly RagSearch CompletionSearch = new(Limit: 5, ScoreThreshold: null);
    private static readonly RagSearch StreamingSearch = new(Limit: 3, ScoreThreshold: 0.65f);

    public PipelineType Type => PipelineType.Baseline;

    /// <summary>Non-streaming answer. Never throws for model errors (see <see cref="IChatStreamingService.CompleteAsync"/>).</summary>
    public async Task<AiCompletionResult> CompleteAsync(ChatTurn turn, CancellationToken cancellationToken = default)
    {
        var preferences = await userPreferenceService.GetPreferencesAsync(turn.UserId, cancellationToken);

        // The completion path always answers with the citizen-oriented system prompt.
        if (!turn.History.Any(m => m.Role == AuthorRole.System))
        {
            turn.History.Insert(0, new ChatMessageContent(AuthorRole.System,
                PromptCatalog.ChatSystem + UserContext(preferences, PromptCatalog.UserContextGuidanceWithDocuments)));
        }

        var hits = await SearchAsync(turn, CompletionSearch, cancellationToken);
        if (hits.Count > 0)
        {
            var date = DateTime.UtcNow.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            ReplaceLastUserMessage(turn, string.Format(CultureInfo.InvariantCulture, PromptCatalog.ChatUserTemplate, FormatContext(hits), turn.UserQuery, date));
        }

        return await chatStreamingService.CompleteAsync(turn.History, turn.TenantId, turn.UserId, Pipeline(turn), cancellationToken);
    }

    public async IAsyncEnumerable<string> StreamAsync(ChatTurn turn, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var preferences = await userPreferenceService.GetPreferencesAsync(turn.UserId, cancellationToken);

        // With context: the RAG prompt and the user's preferences. Without it (small talk), a conversational prompt.
        var hits = await SearchAsync(turn, StreamingSearch, cancellationToken);
        if (hits.Count > 0)
        {
            ReplaceLastUserMessage(turn, string.Format(CultureInfo.InvariantCulture, PromptCatalog.StreamingUserTemplate, FormatContext(hits), turn.UserQuery));
        }
        if (!turn.History.Any(m => m.Role == AuthorRole.System))
        {
            turn.History.Insert(0, new ChatMessageContent(AuthorRole.System, hits.Count > 0
                ? PromptCatalog.StreamingSystem + UserContext(preferences, PromptCatalog.UserContextGuidance)
                : PromptCatalog.ConversationalSystem));
        }

        await foreach (var chunk in chatStreamingService.StreamAsync(turn.History, turn.TenantId, turn.UserId, Pipeline(turn), cancellationToken))
        {
            yield return chunk;
        }

        // The documents sent to the model, after its complete answer: a failed stream throws before reaching here,
        // and a cancelled one just ends, so it is checked.
        var sources = SourceCitations.FromHits(hits);
        if (sources.Length > 0 && !cancellationToken.IsCancellationRequested)
        {
            yield return sources;
        }
    }

    private static string Pipeline(ChatTurn turn) => turn.TelemetryPipeline ?? LlmPipelines.Baseline;

    /// <summary>Public link of a bulletin document (BOJA has no stable per-document URL yet).</summary>
    public static string GetPublicUrl(string source, string documentId) => source.ToUpperInvariant() switch
    {
        "BOE" => $"https://www.boe.es/buscar/doc.php?id={documentId}",
        "BOJA" => "https://www.juntadeandalucia.es/eboja.html",
        _ => $"json/{source}/{documentId}.json"
    };

    /// <summary>
    /// Searches with the platform embedding model (never the tenant's chat model). A failed search degrades to
    /// answering without context instead of failing the request.
    /// </summary>
    private async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(ChatTurn turn, RagSearch search, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(turn.UserQuery))
        {
            return [];
        }

        try
        {
            var embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            var searchText = string.Join("\n", turn.History.Where(m => m.Role == AuthorRole.User).TakeLast(SearchHistoryDepth).Select(m => m.Content));
            var embeddings = await embeddingGenerator.GenerateAsync([searchText], cancellationToken: cancellationToken);

            return await vectorStore.SearchAsync(
                VectorCollection.DocumentChunks,
                new VectorSearchRequest(embeddings[0].Vector.ToArray(), search.Limit, search.ScoreThreshold),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Baseline RAG search failed; answering without retrieved context");
            return [];
        }
    }

    private static string FormatContext(IEnumerable<VectorSearchHit> hits) => string.Join("\n\n---\n\n", hits.Select(hit =>
        $"[Documento: {hit.GetString(VectorPayloadKeys.Title)} | URL: {GetPublicUrl(hit.GetString(VectorPayloadKeys.Source), hit.GetString(VectorPayloadKeys.DocumentId))} | " +
        $"Departamento: {hit.GetString(VectorPayloadKeys.Department)} | Fecha: {hit.GetDate(VectorPayloadKeys.PublicationDate):yyyy-MM-dd}]\n{hit.GetString(VectorPayloadKeys.Content)}"));

    private static string UserContext(UserPreferenceDto preferences, string guidance) =>
        PromptCatalog.UserContext(preferences.Topics, preferences.Locations, guidance);

    private static void ReplaceLastUserMessage(ChatTurn turn, string content) =>
        turn.History[^1] = new ChatMessageContent(AuthorRole.User, content);

    private sealed record RagSearch(int Limit, float? ScoreThreshold);
}
