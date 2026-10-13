using System.Text.Json.Serialization;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

public class ExpandedQueryInfo
{
    [JsonPropertyName("query_lexica")]
    public string LexicalQuery { get; set; } = string.Empty;

    [JsonPropertyName("query_semantica")]
    public string SemanticQuery { get; set; } = string.Empty;

    [JsonPropertyName("filtro_municipio")]
    public string? MunicipalityFilter { get; set; }
}
