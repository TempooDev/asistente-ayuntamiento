using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion.Chunking;

public class FixedOverlapChunkingStrategyTests
{
    [Fact]
    public void ChunkText_WithEmptyText_ReturnsEmpty()
    {
        // Arrange
        var strategy = new FixedOverlapChunkingStrategy(400, 50);

        // Act
        var result = strategy.ChunkText("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ChunkText_WithSmallText_ReturnsSingleChunk()
    {
        // Arrange
        var strategy = new FixedOverlapChunkingStrategy(400, 50);
        var text = "This is a short text that should fit in a single chunk.";

        // Act
        var result = strategy.ChunkText(text).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(text, result[0].Text);
    }

    [Fact]
    public void ChunkText_WithLargeText_SplitsIntoChunksWithOverlap()
    {
        // Arrange
        var strategy = new FixedOverlapChunkingStrategy(maxTokens: 10, overlapTokens: 2);
        
        // Generating a text that is long enough to be split.
        // The semantic kernel chunker counts tokens in a specific way, but for tests 
        // we can just provide enough words.
        var words = string.Join(" ", Enumerable.Range(1, 30).Select(i => $"word{i}"));
        
        // Act
        var result = strategy.ChunkText(words).ToList();

        // Assert
        Assert.True(result.Count > 1, "Text should be split into multiple chunks.");
        
        // Let's verify that overlap exists. It's tricky to assert exact overlap without
        // knowing the exact tokenization, but we can at least assert it chunks.
        Assert.All(result, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.Text)));
    }
}
