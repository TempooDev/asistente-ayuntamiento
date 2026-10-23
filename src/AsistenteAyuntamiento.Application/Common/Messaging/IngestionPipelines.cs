using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Common.Messaging;

/// <summary>Which ingestion pipelines a document is queued for.</summary>
[Flags]
public enum IngestionPipelines
{
    None = 0,
    Baseline = 1,
    Hierarchical = 2,
    Recursive = 4,
    Semantic = 8,
    Both = Baseline | Hierarchical,
    All = Baseline | Hierarchical | Recursive | Semantic,
}

public static class IngestionPipelinesExtensions
{
    private static readonly (IngestionPipelines Flag, PipelineType Pipeline)[] Map =
    [
        (IngestionPipelines.Baseline, PipelineType.Baseline),
        (IngestionPipelines.Hierarchical, PipelineType.Hierarchical),
        (IngestionPipelines.Recursive, PipelineType.Recursive),
        (IngestionPipelines.Semantic, PipelineType.Semantic),
    ];

    /// <summary>The selected pipelines, in <see cref="PipelineType"/> order.</summary>
    public static IReadOnlyList<PipelineType> ToPipelineTypes(this IngestionPipelines pipelines) =>
        Map.Where(m => pipelines.HasFlag(m.Flag)).Select(m => m.Pipeline).ToList();
}
