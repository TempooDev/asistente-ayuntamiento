namespace AsistenteAyuntamiento.Application.Common.Messaging;

public static class IngestionPipelinesParser
{
    /// <summary>Parses the API's <c>pipelineMode</c> (BASELINE | HIERARCHICAL | BOTH, default BOTH).</summary>
    public static IngestionPipelines Parse(string? pipelineMode) => pipelineMode?.Trim().ToUpperInvariant() switch
    {
        "BASELINE" => IngestionPipelines.Baseline,
        "HIERARCHICAL" => IngestionPipelines.Hierarchical,
        _ => IngestionPipelines.Both,
    };
}
