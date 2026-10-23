using Microsoft.SemanticKernel.Text;

namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

public class FixedOverlapChunkingStrategy : IChunkingStrategy
{
    public const int DefaultMaxTokens = 400;
    public const int DefaultOverlapTokens = 50;
    public const int DefaultMaxLineTokens = 200;

    private readonly int _maxTokens;
    private readonly int _overlapTokens;
    private readonly int _maxLineTokens;

    public string StrategyName => ChunkingStrategies.FixedOverlap;

    /// <param name="maxTokens">Maximum tokens per chunk (Ai:Embeddings:ChunkMaxTokens).</param>
    /// <param name="overlapTokens">Tokens shared by consecutive chunks (Ai:Embeddings:ChunkOverlapTokens).</param>
    /// <param name="maxLineTokens">
    /// Maximum tokens per line before lines are grouped into chunks (Ai:Embeddings:ChunkMaxLines).
    /// </param>
    public FixedOverlapChunkingStrategy(
        int maxTokens = DefaultMaxTokens,
        int overlapTokens = DefaultOverlapTokens,
        int maxLineTokens = DefaultMaxLineTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapTokens);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapTokens, maxTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineTokens);

        _maxTokens = maxTokens;
        _overlapTokens = overlapTokens;
        _maxLineTokens = maxLineTokens;
    }

    public Task<IReadOnlyList<DocumentChunkResult>> ChunkTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<IReadOnlyList<DocumentChunkResult>>([]);
        }

        var lines = TextChunker.SplitPlainTextLines(text, _maxLineTokens, ChunkTokenCounter.Count);
        var rawChunks = TextChunker.SplitPlainTextParagraphs(lines, _maxTokens, _overlapTokens, tokenCounter: ChunkTokenCounter.Count);

        IReadOnlyList<DocumentChunkResult> chunks = rawChunks.Select((c, i) => new DocumentChunkResult
        {
            Text = c,
            ChunkIndex = i,
            Metadata = new Dictionary<string, string> { { "Strategy", StrategyName } }
        }).ToList();
        return Task.FromResult(chunks);
    }
}
