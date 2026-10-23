namespace AsistenteAyuntamiento.Application.Common.Messaging;

public static class IngestionPipelinesParser
{
    /// <summary>
    /// Parses the API's <c>pipelineMode</c>: <c>BASELINE</c>, <c>HIERARCHICAL</c>, <c>RECURSIVE</c>, <c>SEMANTIC</c>, a
    /// comma-separated combination of them, <c>BOTH</c> (baseline and hierarchical) or <c>ALL</c> (the four). No value
    /// means <c>BOTH</c>. An unknown name, alone or inside a combination, is rejected (<paramref name="unknown"/> names
    /// it) instead of falling back to a default, so a typo never queues work nobody asked for.
    /// </summary>
    public static bool TryParse(string? pipelineMode, out IngestionPipelines pipelines, out string? unknown)
    {
        pipelines = IngestionPipelines.Both;
        unknown = null;
        if (string.IsNullOrWhiteSpace(pipelineMode))
        {
            return true;
        }

        var result = IngestionPipelines.None;
        foreach (var part in pipelineMode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var flag = part.ToUpperInvariant() switch
            {
                "BASELINE" => IngestionPipelines.Baseline,
                "HIERARCHICAL" => IngestionPipelines.Hierarchical,
                "RECURSIVE" => IngestionPipelines.Recursive,
                "SEMANTIC" => IngestionPipelines.Semantic,
                "BOTH" => IngestionPipelines.Both,
                "ALL" => IngestionPipelines.All,
                _ => IngestionPipelines.None,
            };
            if (flag == IngestionPipelines.None)
            {
                unknown = part;
                return false;
            }
            result |= flag;
        }

        // Only separators (",") carry no name: same as no value.
        pipelines = result == IngestionPipelines.None ? IngestionPipelines.Both : result;
        return true;
    }

    /// <summary>
    /// Like <see cref="TryParse"/>, for callers that already validated the value.
    /// </summary>
    /// <exception cref="ArgumentException">The value names an unknown pipeline.</exception>
    public static IngestionPipelines Parse(string? pipelineMode) =>
        TryParse(pipelineMode, out var pipelines, out var unknown)
            ? pipelines
            : throw new ArgumentException(UnknownPipelineMessage(unknown!), nameof(pipelineMode));

    /// <summary>The validation message for an unknown pipeline name.</summary>
    public static string UnknownPipelineMessage(string unknown) =>
        $"Pipeline desconocido: '{unknown}'. Valores admitidos: BASELINE, HIERARCHICAL, RECURSIVE, SEMANTIC, BOTH, ALL (o una lista separada por comas).";
}
