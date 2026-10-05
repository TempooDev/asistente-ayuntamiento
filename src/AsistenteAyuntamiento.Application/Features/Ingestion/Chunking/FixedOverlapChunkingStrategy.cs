using Microsoft.SemanticKernel.Text;

namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

public class FixedOverlapChunkingStrategy : IChunkingStrategy
{
    private readonly int _maxTokens;
    private readonly int _overlapTokens;

    public FixedOverlapChunkingStrategy(int maxTokens = 400, int overlapTokens = 50)
    {
        _maxTokens = maxTokens;
        _overlapTokens = overlapTokens;
    }

    public IEnumerable<string> ChunkText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Enumerable.Empty<string>();
        }

        var lines = TextChunker.SplitPlainTextLines(text, 200);
        return TextChunker.SplitPlainTextParagraphs(lines, _maxTokens, _overlapTokens);
    }
}
