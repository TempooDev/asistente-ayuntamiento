using Microsoft.Extensions.AI;

namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

/// <summary>Settings of <see cref="SemanticChunkingStrategy"/> (<c>Ai:Chunking:Semantic:*</c>), validated on creation.</summary>
public sealed record SemanticChunkingOptions
{
    public const int DefaultBufferSize = 1;
    public const double DefaultBreakpointPercentile = 95;
    public const int DefaultMinTokens = 100;

    /// <summary>Sentences embedded with each sentence on each side.</summary>
    public int BufferSize { get; }

    /// <summary>A breakpoint goes where the distance between consecutive windows is above this percentile of the document's distances.</summary>
    public double BreakpointPercentile { get; }

    /// <summary>Chunks under this size are merged with the next one.</summary>
    public int MinTokens { get; }

    /// <summary>Chunks over this size are split with the recursive strategy.</summary>
    public int ChunkMaxTokens { get; }

    public SemanticChunkingOptions(
        int bufferSize = DefaultBufferSize,
        double breakpointPercentile = DefaultBreakpointPercentile,
        int minTokens = DefaultMinTokens,
        int chunkMaxTokens = FixedOverlapChunkingStrategy.DefaultMaxTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(breakpointPercentile);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(breakpointPercentile, 100);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkMaxTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(minTokens);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minTokens, chunkMaxTokens);

        BufferSize = bufferSize;
        BreakpointPercentile = breakpointPercentile;
        MinTokens = minTokens;
        ChunkMaxTokens = chunkMaxTokens;
    }
}

/// <summary>
/// Semantic chunking: each sentence is embedded together with its neighbours, and a chunk ends where the cosine
/// distance between consecutive windows is above a percentile of the document's distances (a topic change). Chunks
/// under <c>MinTokens</c> are merged with the next one and chunks over <c>ChunkMaxTokens</c> are split with the
/// recursive strategy (without overlap: semantic chunks do not share text). Windows are documents, so they are
/// embedded without the query instruction. A failed embedding call is thrown, never replaced by another strategy.
/// </summary>
public sealed class SemanticChunkingStrategy(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    SemanticChunkingOptions options) : IChunkingStrategy
{
    internal const int EmbeddingBatchSize = 100;

    private readonly RecursiveChunkingStrategy _oversizeSplitter = new(options.ChunkMaxTokens, overlapTokens: 0);

    public string StrategyName => ChunkingStrategies.Semantic;

    public async Task<IReadOnlyList<DocumentChunkResult>> ChunkTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var sentences = SpanishSentenceSplitter.Split(text);
        if (sentences.Count <= 1 || ChunkTokenCounter.Count(text) <= options.MinTokens)
        {
            return Build([string.Join(" ", sentences)], embeddedTokens: null);
        }

        var windows = Windows(sentences, options.BufferSize);
        var vectors = await EmbedAsync(windows, cancellationToken);

        var distances = new double[sentences.Count - 1];
        for (var i = 0; i < distances.Length; i++)
        {
            distances[i] = 1 - CosineSimilarity(vectors[i], vectors[i + 1]);
        }

        var threshold = Percentile(distances, options.BreakpointPercentile);
        var groups = new List<List<string>> { new() };
        for (var i = 0; i < sentences.Count; i++)
        {
            groups[^1].Add(sentences[i]);
            if (i < distances.Length && distances[i] > threshold)
            {
                groups.Add([]);
            }
        }

        var chunks = MergeSmall(groups.Select(g => string.Join(" ", g)).ToList());
        return Build(chunks, windows.Sum(ChunkTokenCounter.Count));
    }

    /// <summary>Sentence <c>i</c> with <paramref name="buffer"/> sentences on each side.</summary>
    internal static List<string> Windows(IReadOnlyList<string> sentences, int buffer) =>
        Enumerable.Range(0, sentences.Count)
            .Select(i =>
            {
                var from = Math.Max(0, i - buffer);
                var to = Math.Min(sentences.Count - 1, i + buffer);
                return string.Join(" ", sentences.Skip(from).Take(to - from + 1));
            })
            .ToList();

    private async Task<List<ReadOnlyMemory<float>>> EmbedAsync(List<string> windows, CancellationToken cancellationToken)
    {
        var vectors = new List<ReadOnlyMemory<float>>(windows.Count);
        foreach (var batch in windows.Chunk(EmbeddingBatchSize))
        {
            var embeddings = await embeddingGenerator.GenerateAsync(batch, cancellationToken: cancellationToken);
            if (embeddings.Count != batch.Length)
            {
                throw new InvalidOperationException($"The embedding model returned {embeddings.Count} vectors for {batch.Length} sentence windows.");
            }
            vectors.AddRange(embeddings.Select(e => e.Vector));
        }
        return vectors;
    }

    /// <summary>A chunk under the minimum joins the next one; the last one joins the previous.</summary>
    private List<string> MergeSmall(List<string> chunks)
    {
        var merged = new List<string>();
        var pending = string.Empty;
        foreach (var chunk in chunks.Where(c => c.Length > 0))
        {
            pending = pending.Length == 0 ? chunk : pending + " " + chunk;
            if (ChunkTokenCounter.Count(pending) >= options.MinTokens)
            {
                merged.Add(pending);
                pending = string.Empty;
            }
        }

        if (pending.Length > 0)
        {
            if (merged.Count == 0) merged.Add(pending);
            else merged[^1] = merged[^1] + " " + pending;
        }
        return merged;
    }

    private List<DocumentChunkResult> Build(List<string> chunks, int? embeddedTokens)
    {
        var texts = chunks
            .SelectMany(chunk => ChunkTokenCounter.Count(chunk) > options.ChunkMaxTokens
                ? _oversizeSplitter.Chunk(chunk).Select(c => c.Text)
                : [chunk])
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk))
            .ToList();

        return texts.Select((chunk, i) => new DocumentChunkResult
        {
            Text = chunk,
            ChunkIndex = i,
            Metadata = new Dictionary<string, string> { { "Strategy", StrategyName } },
            EmbeddedTokens = i == 0 ? embeddedTokens : null,
        }).ToList();
    }

    internal static double CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var x = a.Span;
        var y = b.Span;
        if (x.Length != y.Length)
        {
            throw new InvalidOperationException("Sentence embeddings of different sizes.");
        }

        double dot = 0, normX = 0, normY = 0;
        for (var i = 0; i < x.Length; i++)
        {
            dot += x[i] * y[i];
            normX += x[i] * x[i];
            normY += y[i] * y[i];
        }
        return normX == 0 || normY == 0 ? 0 : dot / (Math.Sqrt(normX) * Math.Sqrt(normY));
    }

    /// <summary>Percentile with linear interpolation between the closest ranks (NumPy's default).</summary>
    internal static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        var rank = percentile / 100 * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + (rank - lower) * (sorted[upper] - sorted[lower]);
    }
}
