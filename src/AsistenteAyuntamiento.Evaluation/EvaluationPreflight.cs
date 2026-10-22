using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.Extensions.AI;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>
/// Checks before asking any question that every selected variant is ingested with the configured embedding model:
/// its collection exists, has that model's vector size and holds points for every corpus document.
/// </summary>
public sealed class EvaluationPreflight(IVectorStore vectorStore, IEmbeddingGenerator<string, Embedding<float>> embeddings)
{
    /// <summary>The problems found, one per line; empty when the run can start.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(IReadOnlyList<PipelineType> pipelines, IReadOnlyList<string> corpus, CancellationToken cancellationToken = default)
    {
        var probe = await embeddings.GenerateAsync(["prueba"], cancellationToken: cancellationToken);
        var vectorSize = probe[0].Vector.Length;
        var problems = new List<string>();

        foreach (var pipeline in pipelines)
        {
            var collection = EvaluationVariants.Collections[pipeline];
            var stats = await vectorStore.GetStatsAsync(collection, cancellationToken);
            if (stats is null)
            {
                problems.Add($"{pipeline}: collection {collection} does not exist; ingest the corpus with this pipeline.");
                continue;
            }

            if (stats.VectorSize != vectorSize)
            {
                problems.Add($"{pipeline}: collection {collection} has vectors of {stats.VectorSize} dimensions, but the embedding model produces {vectorSize}; drop it and re-ingest.");
                continue;
            }

            var missing = new List<string>();
            foreach (var documentId in corpus)
            {
                if (await vectorStore.CountDocumentPointsAsync(collection, documentId, cancellationToken) == 0)
                {
                    missing.Add(documentId);
                }
            }

            if (missing.Count > 0)
            {
                problems.Add($"{pipeline}: {missing.Count} corpus documents have no points in {collection}: {string.Join(", ", missing)}");
            }
        }

        return problems;
    }
}
