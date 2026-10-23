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
/// recursive strategy (without overlap: semantic chunks do not share text). A "sentence" over <c>ChunkMaxTokens</c>
/// (a table or a list without full stops) is first split the same way, and each window is capped at
/// <see cref="WindowMaxTokens"/> by taking fewer neighbours, so no embedding call exceeds the model's context. Windows
/// are documents, so they are embedded without the query instruction. A failed embedding call is thrown, never replaced by another strategy.
/// </summary>
public sealed class SemanticChunkingStrategy(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    SemanticChunkingOptions options) : IChunkingStrategy
{
    internal const int EmbeddingBatchSize = 100;

    /// <summary>Most chunks of <c>ChunkMaxTokens</c> one sentence window can hold.</summary>
    internal const int WindowMaxChunks = 4;

    /// <summary>Token budget of one sentence window (its sentence always fits: sentences are capped at <c>ChunkMaxTokens</c>).</summary>
    internal int WindowMaxTokens => options.ChunkMaxTokens * WindowMaxChunks;

    private readonly RecursiveChunkingStrategy _oversizeSplitter = new(options.ChunkMaxTokens, overlapTokens: 0);

    public string StrategyName => ChunkingStrategies.Semantic;

    public async Task<IReadOnlyList<DocumentChunkResult>> ChunkTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var sentences = CapSentences(SpanishSentenceSplitter.Split(text));
        if (sentences.Count <= 1 || ChunkTokenCounter.Count(text) <= options.MinTokens)
        {
            return Build([string.Join(" ", sentences)], embeddedTokens: null);
        }

        var windows = Windows(sentences, options.BufferSize, WindowMaxTokens);
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

    /// <summary>Splits every sentence over <c>ChunkMaxTokens</c> with the recursive strategy, without overlap.</summary>
    private List<string> CapSentences(IReadOnlyList<string> sentences) =>
        sentences
            .SelectMany(sentence => ChunkTokenCounter.Count(sentence) > options.ChunkMaxTokens
                ? _oversizeSplitter.Chunk(sentence).Select(c => c.Text)
                : [sentence])
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .ToList();

    /// <summary>
    /// Sentence <c>i</c> with up to <paramref name="buffer"/> sentences on each side, adding neighbours (one on each
    /// side at a time, nearest first) only while the window stays within <paramref name="maxTokens"/>.
    /// </summary>
    internal static List<string> Windows(IReadOnlyList<string> sentences, int buffer, int maxTokens = int.MaxValue) =>
        Enumerable.Range(0, sentences.Count)
            .Select(i =>
            {
                int from = i, to = i;
                var tokens = ChunkTokenCounter.Count(sentences[i]);
                for (var step = 1; step <= buffer; step++)
                {
                    var grew = false;
                    if (i - step >= 0 && from == i - step + 1 && Fits(sentences[i - step]))
                    {
                        from = i - step;
                        grew = true;
                    }
                    if (i + step < sentences.Count && to == i + step - 1 && Fits(sentences[i + step]))
                    {
                        to = i + step;
                        grew = true;
                    }
                    if (!grew) break;
                }
                return string.Join(" ", sentences.Skip(from).Take(to - from + 1));

                bool Fits(string neighbour)
                {
                    var next = tokens + ChunkTokenCounter.Count(neighbour);
                    if (next > maxTokens) return false;
                    tokens = next;
                    return true;
                }
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
