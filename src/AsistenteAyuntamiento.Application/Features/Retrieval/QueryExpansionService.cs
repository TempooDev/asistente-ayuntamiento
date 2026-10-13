using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public class QueryExpansionService(
    [FromKeyedServices("IngestionKernel")] Kernel kernel,
    IConfiguration configuration,
    ILlmUsageMetrics usageMetrics,
    IAiCallRecorder callRecorder,
    ILogger<QueryExpansionService> logger) : IQueryExpansionService
{
    private readonly IChatCompletionService _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
    private readonly (string Provider, string Model) _model = PlatformModels.Ingestion(configuration);

    public async Task<ExpandedQueryInfo> ExpandQueryAsync(string userQuery, LlmCallScope scope, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = string.Format(PromptCatalog.QueryExpansion, userQuery);

            var result = await _chatCompletionService.CompleteMeteredAsync(
                prompt, _model, scope with { Operation = LlmOperations.QueryExpansion }, usageMetrics, cancellationToken, callRecorder);

            return Parse(result.Content, userQuery);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al expandir la consulta: {Query}", userQuery);
            return Fallback(userQuery);
        }
    }

    /// <summary>
    /// Reads the model's JSON answer (optionally inside a ```json fence). Missing queries fall back to the user's
    /// text; unparseable answers throw <see cref="JsonException"/>.
    /// </summary>
    internal static ExpandedQueryInfo Parse(string? modelAnswer, string userQuery)
    {
        var content = (modelAnswer ?? string.Empty).Trim();
        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            content = content[(content.IndexOf('\n') is var newline and >= 0 ? newline + 1 : 3)..];
            if (content.EndsWith("```", StringComparison.Ordinal))
            {
                content = content[..^3];
            }
        }

        var expanded = JsonSerializer.Deserialize<ExpandedQueryInfo>(content.Trim(), JsonOptions) ?? new ExpandedQueryInfo();
        if (string.IsNullOrWhiteSpace(expanded.LexicalQuery)) expanded.LexicalQuery = userQuery;
        if (string.IsNullOrWhiteSpace(expanded.SemanticQuery)) expanded.SemanticQuery = userQuery;
        if (string.IsNullOrWhiteSpace(expanded.MunicipalityFilter)) expanded.MunicipalityFilter = null;
        return expanded;
    }

    /// <summary>Used when the model fails: every word is required lexically, the raw question is used semantically.</summary>
    internal static ExpandedQueryInfo Fallback(string userQuery) => new()
    {
        LexicalQuery = string.Join(" & ", userQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
        SemanticQuery = userQuery
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

