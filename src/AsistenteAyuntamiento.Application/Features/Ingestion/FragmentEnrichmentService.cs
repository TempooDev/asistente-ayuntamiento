using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class FragmentEnrichmentService(
    [FromKeyedServices("IngestionKernel")] Kernel kernel,
    IConfiguration configuration,
    ILlmUsageMetrics usageMetrics,
    ILogger<FragmentEnrichmentService> logger) : IFragmentEnrichmentService
{
    /// <summary>Bulletins are shared by every tenant: the ingestion cost is the platform's.</summary>
    private static readonly LlmCallScope Scope = new(LlmCallScope.PlatformTenant, LlmPipelines.Ingestion, LlmOperations.Enrichment);

    private readonly ILogger<FragmentEnrichmentService> _logger = logger;
    private readonly IChatCompletionService _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
    private readonly (string Provider, string Model) _model = PlatformModels.Ingestion(configuration);

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
        int llmTokens = 0;

        try
        {
            var prompt = string.Format(PromptCatalog.FragmentEnrichment, originalText);

            var result = await _chatCompletionService.CompleteMeteredAsync(prompt, _model, Scope, usageMetrics, cancellationToken);

            syntheticQuestions = result.Content?.Trim() ?? "";
            llmCalls = 1;

            // Real usage when the provider reports it; a rough estimate (4 chars per token) otherwise.
            var usage = TokenUsageExtractor.Extract(result);
            llmTokens = usage.TotalTokens > 0 ? usage.TotalTokens : (prompt.Length + syntheticQuestions.Length) / 4;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando preguntas sintéticas para la sección {Section}.", normSection);
        }

        var enrichedText = $"{breadcrumb}\n\nPREGUNTAS SINTÉTICAS:\n{syntheticQuestions}\n\nCONTENIDO ORIGINAL:\n{originalText}";

        return (enrichedText, llmCalls, llmTokens);
    }
}


