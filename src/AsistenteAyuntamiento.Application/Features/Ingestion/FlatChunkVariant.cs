using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// A flat-chunk RAG variant: the baseline ingestion and answering code with its own chunking strategy, Qdrant
/// collection and job status. Only the chunking differs between variants (chunking study, chapter 7).
/// </summary>
/// <param name="Pipeline">Job status, ingestion metrics and Arena identity.</param>
/// <param name="ChunkingStrategy">Key of the registered <see cref="IChunkingStrategy"/>, also stored in <c>DocumentChunks.ChunkingStrategy</c>.</param>
/// <param name="Collection">Collection searched and written by the variant.</param>
/// <param name="TelemetryPipeline">Value of <c>app.pipeline</c> for the variant's chat answers.</param>
public sealed record FlatChunkVariant(PipelineType Pipeline, string ChunkingStrategy, VectorCollection Collection, string TelemetryPipeline)
{
    public static readonly FlatChunkVariant Baseline =
        new(PipelineType.Baseline, ChunkingStrategies.FixedOverlap, VectorCollection.DocumentChunks, LlmPipelines.Baseline);

    public static readonly FlatChunkVariant Recursive =
        new(PipelineType.Recursive, ChunkingStrategies.Recursive, VectorCollection.RecursiveChunks, LlmPipelines.Recursive);

    public static readonly FlatChunkVariant Semantic =
        new(PipelineType.Semantic, ChunkingStrategies.Semantic, VectorCollection.SemanticChunks, LlmPipelines.Semantic);

    public static IReadOnlyList<FlatChunkVariant> All { get; } = [Baseline, Recursive, Semantic];

    /// <summary>The variant of <paramref name="pipeline"/>, or null for the hierarchical pipeline.</summary>
    public static FlatChunkVariant? For(PipelineType pipeline) => All.FirstOrDefault(v => v.Pipeline == pipeline);
}
