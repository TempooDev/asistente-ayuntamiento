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
    ILogger<ClearLanguageGenerationService> logger) : IClearLanguageGenerationService
{
    private readonly IChatCompletionService _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
    private readonly string _provider = configuration["Ai:Chat:Provider"] ?? "ollama";
    private readonly string _model = configuration["Ai:Chat:Model"] ?? "llama3.2";

    public async Task<string> GenerateResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, LlmCallScope scope, CancellationToken cancellationToken = default)
    {
        var chatHistory = BuildChatHistory(userQuery, retrievedContext);
        if (chatHistory == null)
            return "Lo siento, no he encontrado información legal o normativa relevante en la base de datos para responder a tu consulta.";

        try
        {
            var result = await _chatCompletionService.CompleteMeteredAsync(chatHistory, (_provider, _model), scope, usageMetrics, cancellationToken: cancellationToken);
            return result.Content ?? "Lo siento, hubo un problema al generar la respuesta.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al generar la respuesta en lenguaje claro para la consulta: {Query}", userQuery);
            return "Lo siento, ha ocurrido un error interno al intentar procesar la respuesta. Por favor, inténtalo de nuevo más tarde.";
        }
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GenerateStreamingResponseAsync(
        string userQuery,
        List<RetrievalResult> retrievedContext,
        LlmCallScope scope,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatHistory = BuildChatHistory(userQuery, retrievedContext);
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
        string? errorType = null;

        IAsyncEnumerator<StreamingChatMessageContent>? enumerator = null;
        try
        {
            try
            {
                enumerator = _chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory, cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not start the plain-language answer stream");
                errorType = ex.GetType().Name;
            }

            if (enumerator is null)
            {
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, "Lo siento, ha ocurrido un error interno al intentar procesar la respuesta. Por favor, inténtalo de nuevo más tarde.");
                yield break;
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
                    errorType = ex.GetType().Name;
                    throw;
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
            usageMetrics.Record(new LlmCallMetrics(_provider, _model, scope.TenantId, scope.Pipeline, Fallback: false,
                stopwatch.Elapsed, usage.InputTokens, usage.OutputTokens, timeToFirstToken, errorType, scope.Operation));
        }
    }

    private ChatHistory? BuildChatHistory(string userQuery, List<RetrievalResult> retrievedContext)
    {
        if (!retrievedContext.Any())
        {
            return null;
        }

        var contextBuilder = new StringBuilder();
        var uniqueParents = retrievedContext
            .GroupBy(r => r.ParentId)
            .Select(g => g.First())
            .ToList();

        foreach (var ctx in uniqueParents)
        {
            contextBuilder.AppendLine("--- INICIO DE DOCUMENTO LEGAL ---");
            contextBuilder.AppendLine(DocumentLabel(ctx));
            contextBuilder.AppendLine(ctx.ParentFullText);
            contextBuilder.AppendLine("--- FIN DE DOCUMENTO LEGAL ---\n");
        }

        var systemPrompt = string.Format(PromptCatalog.ClearLanguageGeneration, contextBuilder.ToString());
        var chatHistory = new ChatHistory(systemPrompt);
        chatHistory.AddUserMessage(userQuery);

        return chatHistory;
    }

    /// <summary>
    /// Header of a parent document in the prompt: title, bulletin, identifier, issuing body, date and public URL (the
    /// same link as the baseline pipeline), so the model can cite the norm in "Fuentes consultadas".
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
        label.Append($" | URL: {BaselineRagPipeline.GetPublicUrl(bulletin.ToString(), result.ParentDocumentId)}]");
        return label.ToString();
    }
}
