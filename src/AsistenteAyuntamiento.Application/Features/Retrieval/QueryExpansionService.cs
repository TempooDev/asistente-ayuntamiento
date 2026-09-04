using System.Text.Json;
using System.Text.Json.Serialization;
using AsistenteAyuntamiento.Application.Common.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public class ExpandedQueryInfo
{
    [JsonPropertyName("query_lexica")]
    public string QueryLexica { get; set; } = string.Empty;

    [JsonPropertyName("query_semantica")]
    public string QuerySemantica { get; set; } = string.Empty;

    [JsonPropertyName("filtro_municipio")]
    public string? FiltroMunicipio { get; set; }
}

public interface IQueryExpansionService
{
    Task<ExpandedQueryInfo> ExpandQueryAsync(string userQuery, CancellationToken cancellationToken = default);
}

public class QueryExpansionService : IQueryExpansionService
{
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ILogger<QueryExpansionService> _logger;

    public QueryExpansionService(Kernel kernel, ILogger<QueryExpansionService> logger)
    {
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
    }

    public async Task<ExpandedQueryInfo> ExpandQueryAsync(string userQuery, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = string.Format(SystemPrompts.QueryExpansion, userQuery);

            var result = await _chatCompletionService.GetChatMessageContentAsync(
                prompt,
                cancellationToken: cancellationToken);

            var content = result.Content?.Trim() ?? "";

            // Clean up possible markdown code blocks
            if (content.StartsWith("```json"))
            {
                content = content.Substring(7);
                if (content.EndsWith("```"))
                {
                    content = content.Substring(0, content.Length - 3);
                }
            }
            content = content.Trim();

            var expanded = JsonSerializer.Deserialize<ExpandedQueryInfo>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return expanded ?? new ExpandedQueryInfo
            {
                QueryLexica = userQuery.Replace(" ", " & "),
                QuerySemantica = userQuery
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al expandir la consulta: {Query}", userQuery);
            return new ExpandedQueryInfo
            {
                QueryLexica = string.Join(" & ", userQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
                QuerySemantica = userQuery
            };
        }
    }
}
