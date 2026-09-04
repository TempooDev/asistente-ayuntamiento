using System.Text;
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

        var systemPrompt = @"
Eres un asistente experto del Ayuntamiento diseñado para explicar normativas legales (BOE, BOJA, etc.) a ciudadanos sin formación jurídica.
Tu objetivo es traducir el texto legal a 'lenguaje claro' (Plain Language).

REGLAS ESTRICTAS:
1. **Lenguaje Amigable**: Usa un tono servicial, directo y fácil de entender. Háblale de 'tú' al ciudadano.
2. **Estructura Clara**: Utiliza encabezados markdown (##), listas con viñetas y negritas para resaltar puntos clave (fechas, requisitos, cuantías).
3. **Explicación de Jerga**: Si debes usar un término legal o administrativo complejo (ej. 'silencio administrativo', 'prorrateo'), explícalo brevemente entre paréntesis.
4. **Cita de Fuentes**: Al final de tu respuesta, añade una sección 'Fuentes consultadas' e indica en qué artículos o leyes te has basado.
5. **No Inventes**: Basa tu respuesta ÚNICAMENTE en los documentos legales proporcionados. Si los documentos no contienen la respuesta, di claramente que no dispones de esa información. No respondas en base a tu conocimiento previo.

DOCUMENTOS LEGALES PROPORCIONADOS:
" + contextBuilder.ToString();

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
