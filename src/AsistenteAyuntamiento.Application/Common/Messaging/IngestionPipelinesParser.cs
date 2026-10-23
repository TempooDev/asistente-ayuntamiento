namespace AsistenteAyuntamiento.Application.Common.Messaging;

public static class IngestionPipelinesParser
{
    /// <summary>
    /// Parses the API's <c>pipelineMode</c>: <c>BASELINE</c>, <c>HIERARCHICAL</c>, <c>RECURSIVE</c>, <c>SEMANTIC</c>, a
    /// comma-separated combination of them, <c>BOTH</c> (baseline and hierarchical) or <c>ALL</c> (the four). Anything
    /// else, including an unknown name inside a combination, or no value means <c>BOTH</c>.
    /// </summary>
    public static IngestionPipelines Parse(string? pipelineMode)
    {
        if (string.IsNullOrWhiteSpace(pipelineMode))
        {
            return IngestionPipelines.Both;
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
                return IngestionPipelines.Both;
            }
            result |= flag;
        }

        return result == IngestionPipelines.None ? IngestionPipelines.Both : result;
    }
}
