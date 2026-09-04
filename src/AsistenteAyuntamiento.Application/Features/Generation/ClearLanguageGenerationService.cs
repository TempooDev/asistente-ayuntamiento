using System.Text;
using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Generation;

public interface IClearLanguageGenerationService
{
    Task<string> GenerateResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, CancellationToken cancellationToken = default);
    IAsyncEnumerable<StreamingChatMessageContent> GenerateStreamingResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, CancellationToken cancellationToken = default);
}

public class ClearLanguageGenerationService : IClearLanguageGenerationService
{
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ILogger<ClearLanguageGenerationService> _logger;

    public ClearLanguageGenerationService(Kernel kernel, ILogger<ClearLanguageGenerationService> logger)
    {
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
    }

    public async Task<string> GenerateResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, CancellationToken cancellationToken = default)
    {
        var chatHistory = BuildChatHistory(userQuery, retrievedContext);
        if (chatHistory == null)
            return "Lo siento, no he encontrado información legal o normativa relevante en la base de datos para responder a tu consulta.";

        try
        {
            var result = await _chatCompletionService.GetChatMessageContentAsync(chatHistory, cancellationToken: cancellationToken);
            return result.Content ?? "Lo siento, hubo un problema al generar la respuesta.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al generar la respuesta en lenguaje claro para la consulta: {Query}", userQuery);
            return "Lo siento, ha ocurrido un error interno al intentar procesar la respuesta. Por favor, inténtalo de nuevo más tarde.";
        }
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GenerateStreamingResponseAsync(string userQuery, List<RetrievalResult> retrievedContext, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatHistory = BuildChatHistory(userQuery, retrievedContext);
        if (chatHistory == null)
        {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, "Lo siento, no he encontrado información legal o normativa relevante en la base de datos para responder a tu consulta.");
            yield break;
        }

        IAsyncEnumerable<StreamingChatMessageContent> stream;
        try
        {
            stream = _chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al iniciar el stream de respuesta para la consulta: {Query}", userQuery);
            stream = null;
        }

        if (stream == null)
        {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, "Lo siento, ha ocurrido un error interno al intentar procesar la respuesta. Por favor, inténtalo de nuevo más tarde.");
            yield break;
        }

        await foreach (var chunk in stream)
        {
            yield return chunk;
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
            contextBuilder.AppendLine($"ID Documento Padre: {ctx.ParentId}");
            contextBuilder.AppendLine(ctx.ParentFullText);
            contextBuilder.AppendLine("--- FIN DE DOCUMENTO LEGAL ---\n");
        }

        var systemPrompt = string.Format(SystemPrompts.ClearLanguageGeneration, contextBuilder.ToString());
        var chatHistory = new ChatHistory(systemPrompt);
        chatHistory.AddUserMessage(userQuery);
        
        return chatHistory;
    }
}
