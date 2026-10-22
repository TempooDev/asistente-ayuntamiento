using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>The vector collection each RAG variant searches.</summary>
public static class EvaluationVariants
{
    public static readonly IReadOnlyDictionary<PipelineType, VectorCollection> Collections = new Dictionary<PipelineType, VectorCollection>
    {
        [PipelineType.Baseline] = VectorCollection.DocumentChunks,
        [PipelineType.Hierarchical] = VectorCollection.ChildFragments,
    };

    /// <summary>Parses <c>--pipelines Baseline,Hierarchical</c>; empty means every variant.</summary>
    public static IReadOnlyList<PipelineType> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [.. Collections.Keys];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => Enum.TryParse<PipelineType>(name, ignoreCase: true, out var type) && Collections.ContainsKey(type)
                ? type
                : throw new ArgumentException($"Unknown pipeline '{name}'. Valid: {string.Join(", ", Collections.Keys)}."))
            .Distinct()
            .ToList();
    }
}
