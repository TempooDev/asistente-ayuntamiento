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
        if (!retrievedContext.Any())
        {
            return "Lo siento, no he encontrado información legal o normativa relevante en la base de datos para responder a tu consulta.";
        }

        var contextBuilder = new StringBuilder();
        // Since parents might be duplicated if multiple children hit the same parent, we need to deduplicate by ParentId
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

        try
        {
            var chatHistory = new ChatHistory(systemPrompt);
            chatHistory.AddUserMessage(userQuery);

            var result = await _chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                cancellationToken: cancellationToken);

            return result.Content ?? "Lo siento, hubo un problema al generar la respuesta.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al generar la respuesta en lenguaje claro para la consulta: {Query}", userQuery);
            return "Lo siento, ha ocurrido un error interno al intentar procesar la respuesta. Por favor, inténtalo de nuevo más tarde.";
        }
    }
}
