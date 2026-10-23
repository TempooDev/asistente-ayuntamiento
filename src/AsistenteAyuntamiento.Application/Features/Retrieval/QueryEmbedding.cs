using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

/// <summary>
/// Task instruction prepended to search queries (<c>PromptCatalog.EmbeddingQueryInstruction</c>, enabled with
/// <c>Ai:Embeddings:UseQueryInstruction</c>). Instruction-aware embedding models such as Qwen3-Embedding rank better with
/// <c>Instruct: {task}\nQuery: {text}</c> on the query side, while documents are embedded as they are. Null means
/// queries are embedded unchanged (providers that do not use it).
/// </summary>
public sealed record QueryEmbeddingOptions(string? Instruction)
{
    public string Format(string query) =>
        string.IsNullOrWhiteSpace(Instruction) ? query : $"Instruct: {Instruction.Trim()}\nQuery: {query}";

    /// <summary>The options registered in the app's services; null for kernels built without them (unit tests).</summary>
    public static QueryEmbeddingOptions? From(Kernel kernel) => kernel.Services.GetService<QueryEmbeddingOptions>();
}

public static class QueryEmbeddingExtensions
{
    /// <summary>Embeds a search query, with the configured instruction if there is one. Never use it for documents.</summary>
    public static async Task<float[]> EmbedQueryAsync(this IEmbeddingGenerator<string, Embedding<float>> generator, string query,
        QueryEmbeddingOptions? options, CancellationToken cancellationToken = default)
    {
        var embeddings = await generator.GenerateAsync([options?.Format(query) ?? query], cancellationToken: cancellationToken);
        return embeddings[0].Vector.ToArray();
    }
}
