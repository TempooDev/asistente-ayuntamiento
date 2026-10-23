using System.Diagnostics;
using System.Text;
using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Generation;

/// <summary>Plain-language answers of the hierarchical pipeline, generated with the platform chat model (<c>Ai:Chat:*</c>).</summary>
public class ClearLanguageGenerationService(
    Kernel kernel,
    IConfiguration configuration,
    ILlmUsageMetrics usageMetrics,
    IAiCallRecorder callRecorder,
    ILogger<ClearLanguageGenerationService> logger) : IClearLanguageGenerationService
{
    private readonly IChatCompletionService _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
    private readonly string _provider = configuration["Ai:Chat:Provider"] ?? "ollama";
    private readonly string _model = configuration["Ai:Chat:Model"] ?? "llama3.2";
    private readonly int _maxContextChars = configuration.GetValue("Ai:Hierarchical:MaxContextChars", DefaultMaxContextChars);

    // Only when configured (the evaluation runner sets 0): otherwise the model keeps its own default, as before.
    private readonly PromptExecutionSettings? _settings = configuration.GetValue<double?>("Ai:Chat:Temperature") is { } temperature
        ? new PromptExecutionSettings { ExtensionData = new Dictionary<string, object> { ["Temperature"] = temperature } }
        : null;

    /// <summary>
    /// Characters of legal text sent per answer (about 6,500 tokens). Parent documents average 14,000 characters
    /// and reach 100,000: whole, five of them overflow small context windows (Ollama then drops the system prompt,
    /// documents included, without an error) and cost tens of thousands of tokens per answer elsewhere.
    /// </summary>
    internal const int DefaultMaxContextChars = 24_000;

    public async Task<string> GenerateResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, LlmCallScope scope, CancellationToken cancellationToken = default)
    {
        var (chatHistory, sources) = BuildChatHistory(userQuery, retrievedContext);
        if (chatHistory == null)
            return "Lo siento, no he encontrado información legal o normativa relevante en la base de datos para responder a tu consulta.";

        // A model failure is recorded and rethrown: the caller (the Arena page) must not present it as an answer.
        var result = await _chatCompletionService.CompleteMeteredAsync(chatHistory, (_provider, _model), scope, usageMetrics, cancellationToken: cancellationToken, recorder: callRecorder);
        return (result.Content ?? string.Empty) + sources;
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GenerateStreamingResponseAsync(
        string userQuery,
        List<RetrievalResult> retrievedContext,
        LlmCallScope scope,
        IReadOnlyList<ChatMessageContent>? previousMessages = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (chatHistory, sources) = BuildChatHistory(userQuery, retrievedContext, previousMessages);
        if (chatHistory == null)
        {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, "Lo siento, no he encontrado información legal o normativa relevante en la base de datos para responder a tu consulta.");
            yield break;
        }

        using var activity = AiMetricsService.ActivitySource.StartActivity("AI.ChatCompletion.Stream");
        activity?.SetTag(GenAiTelemetry.Attributes.OperationName, GenAiTelemetry.ChatOperation);
        activity?.SetTag(GenAiTelemetry.Attributes.ProviderName, GenAiTelemetry.ProviderName(_provider));
        activity?.SetTag(GenAiTelemetry.Attributes.RequestModel, _model);
        activity?.SetTag(GenAiTelemetry.Attributes.TenantId, scope.TenantId);
        activity?.SetTag(GenAiTelemetry.Attributes.Pipeline, scope.Pipeline);
        activity?.SetTag(GenAiTelemetry.Attributes.Operation, scope.Operation);

        var stopwatch = Stopwatch.StartNew();
        TimeSpan? timeToFirstToken = null;
        var usage = TokenUsageInfo.Empty;
        var responseLength = 0;
        string? errorType = null;
        string? errorDetail = null;

        IAsyncEnumerator<StreamingChatMessageContent>? enumerator = null;
        try
        {
            try
            {
                enumerator = _chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory, _settings, cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Typed like the baseline: the chat shows it as the answer, the Arena marks this side as failed.
                logger.LogError(ex, "Could not start the plain-language answer stream");
                errorType = LlmErrorClassifier.Classify(ex);
                errorDetail = ex.Message;
                throw new LlmStreamInterruptedException(errorType, ex);
            }

            while (true)
            {
                bool hasChunk;
                try
                {
                    hasChunk = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errorType = LlmErrorClassifier.Classify(ex);
                    errorDetail = ex.Message;
                    throw new LlmStreamInterruptedException(errorType, ex);
                }
                if (!hasChunk) break;

                var chunk = enumerator.Current;
                if (timeToFirstToken is null && !string.IsNullOrEmpty(chunk.Content))
                {
                    timeToFirstToken = stopwatch.Elapsed;
                }
                var chunkUsage = TokenUsageExtractor.Extract(chunk.Metadata, chunk.InnerContent);
                if (chunkUsage.TotalTokens > 0)
                {
                    usage = chunkUsage;
                }
                responseLength += chunk.Content?.Length ?? 0;
                yield return chunk;
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync();
            }

            if (errorType is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, errorType);
            }
            activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
            activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);

            if (scope.UserId is null)
            {
                usageMetrics.Record(new LlmCallMetrics(_provider, _model, scope.TenantId, scope.Pipeline, Fallback: false,
                    stopwatch.Elapsed, usage.InputTokens, usage.OutputTokens, timeToFirstToken, errorType, scope.Operation));
            }
            else
            {
                // A user's answer: also persisted in AiCallLogs like the baseline's (the recorder emits the metrics too).
                await callRecorder.RecordAsync(
                    new AiCallOutcome(_model, scope.TenantId, scope.UserId, errorType is null, stopwatch.Elapsed.TotalMilliseconds, usage,
                        userQuery.Length, responseLength, chatHistory.Count, errorType, errorDetail)
                    {
                        Provider = _provider,
                        Pipeline = scope.Pipeline,
                        TimeToFirstToken = timeToFirstToken,
                        Operation = scope.Operation,
                    },
                    CancellationToken.None);
            }
        }

        // Only after a complete answer: a failed stream throws above and the Arena marks the side as failed.
        if (sources.Length > 0)
        {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, sources);
        }
    }

    public string BuildPromptContext(List<RetrievalResult> retrievedContext) =>
        retrievedContext.Count == 0 ? string.Empty : BuildContext(retrievedContext, _maxContextChars).Text;

    /// <summary>The prompt, and the "Fuentes consultadas" of the documents it includes (not of those left out).</summary>
    private (ChatHistory? History, string Sources) BuildChatHistory(string userQuery, List<RetrievalResult> retrievedContext,
        IReadOnlyList<ChatMessageContent>? previousMessages = null)
    {
        if (!retrievedContext.Any())
        {
            return (null, string.Empty);
        }

        var context = BuildContext(retrievedContext, _maxContextChars);
        if (context.Excerpted > 0)
        {
            logger.LogInformation("Hierarchical context over {MaxChars} characters: {Excerpted} of {Documents} documents sent as their matching fragments",
                _maxContextChars, context.Excerpted, context.Included.Count);
        }

        var systemPrompt = string.Format(PromptCatalog.ClearLanguageGeneration, context.Text);
        var chatHistory = new ChatHistory(systemPrompt);
        chatHistory.AddRange(previousMessages ?? []);
        chatHistory.AddUserMessage(userQuery);

        return (chatHistory, SourceCitations.FromRetrieval(context.Included));
    }

    /// <summary>
    /// The legal text of the prompt, best-ranked parent first and within <paramref name="maxChars"/>: a parent that
    /// fits goes whole; one that does not is replaced by its retrieved fragments (the passages that matched the
    /// query), cut at the budget. The best-ranked document always contributes something. <c>Included</c> holds one
    /// result per parent sent, for the sources.
    /// </summary>
    internal static (string Text, IReadOnlyList<RetrievalResult> Included, int Excerpted) BuildContext(IReadOnlyList<RetrievalResult> retrieved, int maxChars)
    {
        var builder = new StringBuilder();
        var remaining = maxChars;
        var included = new List<RetrievalResult>();
        var excerpted = 0;

        // GroupBy keeps the order in which each parent first appears, i.e. its best rank.
        foreach (var parent in retrieved.GroupBy(r => r.ParentId))
        {
            if (remaining <= 0) break;

            var first = parent.First();
            string body;
            if (first.ParentFullText.Length <= remaining)
            {
                body = first.ParentFullText;
            }
            else
            {
                var fragments = string.Join("\n[...]\n", parent.Select(r => r.ChunkText).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct());
                body = "(Extractos relevantes; el texto completo es demasiado extenso)\n" + fragments;
                if (body.Length > remaining) body = body[..remaining];
                excerpted++;
            }

            builder.AppendLine("--- INICIO DE DOCUMENTO LEGAL ---");
            builder.AppendLine(DocumentLabel(first));
            builder.AppendLine(body);
            builder.AppendLine("--- FIN DE DOCUMENTO LEGAL ---\n");
            remaining -= body.Length;
            included.Add(first);
        }

        return (builder.ToString(), included, excerpted);
    }

    /// <summary>
    /// Header of a parent document in the prompt: title, bulletin, identifier, issuing body, date and public URL (the
    /// same link as the baseline pipeline), so the model can cite the norm in its answer.
    /// </summary>
    internal static string DocumentLabel(RetrievalResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ParentDocumentId) || result.Bulletin is not { } bulletin)
        {
            // Without the parent's data, the database id is the only reference available.
            return $"[Documento: {(string.IsNullOrWhiteSpace(result.ParentTitle) ? $"ID {result.ParentId}" : result.ParentTitle)}]";
        }

        var title = string.IsNullOrWhiteSpace(result.ParentTitle) ? result.ParentDocumentId : result.ParentTitle;
        var label = new StringBuilder($"[Documento: {title} | Boletín: {bulletin} | Referencia: {result.ParentDocumentId}");
        if (!string.IsNullOrWhiteSpace(result.IssuingBody)) label.Append($" | Organismo: {result.IssuingBody}");
        if (result.PublicationDate is { } date) label.Append($" | Fecha: {date:yyyy-MM-dd}");
        label.Append($" | URL: {FlatChunkRagPipeline.GetPublicUrl(bulletin.ToString(), result.ParentDocumentId)}]");
        return label.ToString();
    }
}
