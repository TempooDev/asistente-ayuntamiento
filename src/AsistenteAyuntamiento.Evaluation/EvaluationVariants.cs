using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>The vector collection each RAG variant searches.</summary>
public static class EvaluationVariants
{
    /// <summary>The four variants of the chunking study, in <see cref="PipelineType"/> order.</summary>
    public static readonly IReadOnlyDictionary<PipelineType, VectorCollection> Collections = new Dictionary<PipelineType, VectorCollection>
    {
        [PipelineType.Baseline] = FlatChunkVariant.Baseline.Collection,
        [PipelineType.Hierarchical] = VectorCollection.ChildFragments,
        [PipelineType.Recursive] = FlatChunkVariant.Recursive.Collection,
        [PipelineType.Semantic] = FlatChunkVariant.Semantic.Collection,
    };

    /// <summary>Parses <c>--pipelines Baseline,Recursive</c>; empty means the four variants.</summary>
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
