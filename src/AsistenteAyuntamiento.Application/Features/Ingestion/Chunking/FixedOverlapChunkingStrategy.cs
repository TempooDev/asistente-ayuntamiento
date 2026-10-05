using Microsoft.SemanticKernel.Text;

namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

public class FixedOverlapChunkingStrategy : IChunkingStrategy
{
    private readonly int _maxTokens;
    private readonly int _overlapTokens;

    public string StrategyName => "FixedOverlap";

    public FixedOverlapChunkingStrategy(int maxTokens = 400, int overlapTokens = 50)
    {
        _maxTokens = maxTokens;
        _overlapTokens = overlapTokens;
    }

    public IEnumerable<DocumentChunkResult> ChunkText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Enumerable.Empty<DocumentChunkResult>();
        }

        var lines = TextChunker.SplitPlainTextLines(text, 200);
        var rawChunks = TextChunker.SplitPlainTextParagraphs(lines, _maxTokens, _overlapTokens);
        
        return rawChunks.Select((c, i) => new DocumentChunkResult
        {
            Text = c,
            ChunkIndex = i,
            Metadata = new Dictionary<string, string> { { "Strategy", StrategyName } }
        });
    }
}
