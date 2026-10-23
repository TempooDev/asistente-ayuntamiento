namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

/// <summary>
/// Splits by the largest unit that fits: paragraphs, then lines, then sentence ends, then words and, as a last
/// resort, characters. Pieces keep their trailing separator, so a chunk is always a contiguous span of the text.
/// Adjacent pieces are merged up to <c>ChunkMaxTokens</c>, and a new chunk starts with the whole pieces at the end of
/// the previous one that fit in <c>ChunkOverlapTokens</c>. Same token estimate as the fixed strategy.
/// </summary>
public sealed class RecursiveChunkingStrategy : IChunkingStrategy
{
    /// <summary>Tried in order; a piece that is still too long is split again with the next one.</summary>
    internal static readonly string[] Separators = ["\n\n", "\n", ". ", "; ", ": ", " "];

    private readonly int _maxTokens;
    private readonly int _overlapTokens;

    public string StrategyName => ChunkingStrategies.Recursive;

    /// <param name="maxTokens">Maximum tokens per chunk (Ai:Chunking:Recursive:ChunkMaxTokens, else Ai:Embeddings:ChunkMaxTokens).</param>
    /// <param name="overlapTokens">Maximum tokens repeated from the previous chunk (Ai:Chunking:Recursive:ChunkOverlapTokens, else Ai:Embeddings:ChunkOverlapTokens).</param>
    public RecursiveChunkingStrategy(
        int maxTokens = FixedOverlapChunkingStrategy.DefaultMaxTokens,
        int overlapTokens = FixedOverlapChunkingStrategy.DefaultOverlapTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapTokens);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapTokens, maxTokens);

        _maxTokens = maxTokens;
        _overlapTokens = overlapTokens;
    }

    public Task<IReadOnlyList<DocumentChunkResult>> ChunkTextAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(Chunk(text));

    /// <summary>Synchronous core, also used by the semantic strategy to split chunks over its limit.</summary>
    internal IReadOnlyList<DocumentChunkResult> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var pieces = Split(text.Replace("\r\n", "\n"), 0);
        return Merge(pieces)
            .Select(chunk => chunk.Trim())
            .Where(chunk => chunk.Length > 0)
            .Select((chunk, i) => new DocumentChunkResult
            {
                Text = chunk,
                ChunkIndex = i,
                Metadata = new Dictionary<string, string> { { "Strategy", StrategyName } }
            })
            .ToList();
    }

    private bool Fits(int characters) => characters >> 2 <= _maxTokens;

    /// <summary>Pieces that each fit in the limit, in order, whose concatenation is <paramref name="text"/>.</summary>
    private List<string> Split(string text, int separatorIndex)
    {
        if (Fits(text.Length))
        {
            return [text];
        }

        if (separatorIndex >= Separators.Length)
        {
            // No separator left: cut by characters.
            var size = ChunkTokenCounter.MaxCharacters(_maxTokens);
            var cuts = new List<string>();
            for (var start = 0; start < text.Length; start += size)
            {
                cuts.Add(text.Substring(start, Math.Min(size, text.Length - start)));
            }
            return cuts;
        }

        var parts = SplitKeepingSeparator(text, Separators[separatorIndex]);
        if (parts.Count == 1)
        {
            return Split(text, separatorIndex + 1);
        }

        var result = new List<string>();
        foreach (var part in parts)
        {
            if (Fits(part.Length))
            {
                result.Add(part);
            }
            else
            {
                result.AddRange(Split(part, separatorIndex + 1));
            }
        }
        return result;
    }

    private static List<string> SplitKeepingSeparator(string text, string separator)
    {
        var parts = new List<string>();
        var start = 0;
        int index;
        while ((index = text.IndexOf(separator, start, StringComparison.Ordinal)) >= 0)
        {
            var end = index + separator.Length;
            parts.Add(text[start..end]);
            start = end;
        }
        if (start < text.Length)
        {
            parts.Add(text[start..]);
        }
        return parts;
    }

    /// <summary>Greedy merge of adjacent pieces, starting each chunk with the overlap taken from the previous one.</summary>
    private List<string> Merge(List<string> pieces)
    {
        var chunks = new List<string>();
        var current = new List<string>();
        var currentLength = 0;

        foreach (var piece in pieces)
        {
            if (current.Count > 0 && !Fits(currentLength + piece.Length))
            {
                chunks.Add(string.Concat(current));

                // Whole pieces from the end of the chunk, while they fit in the overlap and leave room for this piece.
                var overlap = new List<string>();
                var overlapLength = 0;
                for (var i = current.Count - 1; i >= 0; i--)
                {
                    var length = overlapLength + current[i].Length;
                    if (length >> 2 > _overlapTokens || !Fits(length + piece.Length))
                    {
                        break;
                    }
                    overlap.Insert(0, current[i]);
                    overlapLength = length;
                }

                current = overlap;
                currentLength = overlapLength;
            }

            current.Add(piece);
            currentLength += piece.Length;
        }

        if (current.Count > 0)
        {
            chunks.Add(string.Concat(current));
        }

        return chunks;
    }
}
