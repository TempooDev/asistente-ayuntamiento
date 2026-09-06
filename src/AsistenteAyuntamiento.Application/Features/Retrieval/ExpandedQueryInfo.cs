using System.Text.Json.Serialization;

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
