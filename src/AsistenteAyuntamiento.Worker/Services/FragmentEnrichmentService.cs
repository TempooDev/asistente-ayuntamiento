using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.Logging;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Worker.Services;

public class FragmentEnrichmentService(Kernel kernel, ILogger<FragmentEnrichmentService> logger) : IFragmentEnrichmentService
{
    private readonly Kernel _kernel = kernel;
    private readonly ILogger<FragmentEnrichmentService> _logger = logger;
    private readonly IChatCompletionService _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

    public async Task<(string EnrichedText, int LlmCalls, int LlmTokens)> EnrichFragmentAsync(
        BulletinType bulletin,
        string issuingBody,
        string normTitle,
        string normSection,
        string subSection,
        string originalText,
        CancellationToken cancellationToken = default)
    {
        var breadcrumb = $"[BOLETÍN: {bulletin} | ORGANISMO: {issuingBody} | NORMA: {normTitle} | ARTÍCULO: {normSection} | SUBSECCIÓN: {subSection}]";

        // Generate synthetic questions
        string syntheticQuestions = "";
        int llmCalls = 0;
        int llmTokens = 0; // Semantic Kernel doesn't always expose token counts easily without native results, we'll estimate or leave as 0 if unavailable

        try
        {
            var prompt = string.Format(AsistenteAyuntamiento.Application.Common.Prompts.SystemPrompts.FragmentEnrichment, originalText);

            var result = await _chatCompletionService.GetChatMessageContentAsync(
                prompt,
                cancellationToken: cancellationToken);

            syntheticQuestions = result.Content?.Trim() ?? "";
            llmCalls = 1;

            // Note: In a real scenario we'd parse usage metadata to get llmTokens, estimating here.
            llmTokens = (prompt.Length + syntheticQuestions.Length) / 4;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando preguntas sintéticas para la sección {Section}.", normSection);
        }

        var enrichedText = $"{breadcrumb}\n\nPREGUNTAS SINTÉTICAS:\n{syntheticQuestions}\n\nCONTENIDO ORIGINAL:\n{originalText}";

        return (enrichedText, llmCalls, llmTokens);
    }
}
