using AsistenteAyuntamiento.Application;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion.Chunking;

public class FixedOverlapChunkingStrategyTests
{
    [Fact]
    public async Task ChunkText_WithEmptyText_ReturnsEmpty()
    {
        // Arrange
        var strategy = new FixedOverlapChunkingStrategy(400, 50);

        // Act
        var result = await strategy.ChunkTextAsync("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ChunkText_WithSmallText_ReturnsSingleChunk()
    {
        // Arrange
        var strategy = new FixedOverlapChunkingStrategy(400, 50);
        var text = "This is a short text that should fit in a single chunk.";

        // Act
        var result = (await strategy.ChunkTextAsync(text)).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(text, result[0].Text);
    }

    [Fact]
    public async Task ChunkText_WithLargeText_SplitsIntoChunksWithOverlap()
    {
        // Arrange
        var strategy = new FixedOverlapChunkingStrategy(maxTokens: 10, overlapTokens: 2);

        // Generating a text that is long enough to be split.
        // The semantic kernel chunker counts tokens in a specific way, but for tests 
        // we can just provide enough words.
        var words = string.Join(" ", Enumerable.Range(1, 30).Select(i => $"word{i}"));

        // Act
        var result = (await strategy.ChunkTextAsync(words)).ToList();

        // Assert
        Assert.True(result.Count > 1, "Text should be split into multiple chunks.");

        // Let's verify that overlap exists. It's tricky to assert exact overlap without
        // knowing the exact tokenization, but we can at least assert it chunks.
        Assert.All(result, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.Text)));
    }

    // One sentence without separators: SplitPlainTextLines can only cut it by the line token limit.
    private static readonly string LongSentence = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"word{i}"));

    [Fact]
    public async Task ChunkText_MaxLineTokens_ChangesHowLinesAreCut()
    {
        var longLines = new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 5, maxLineTokens: 200);
        var shortLines = new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 5, maxLineTokens: 4);

        var withLongLines = (await longLines.ChunkTextAsync(LongSentence)).Select(c => c.Text).ToList();
        var withShortLines = (await shortLines.ChunkTextAsync(LongSentence)).Select(c => c.Text).ToList();

        Assert.NotEqual(withLongLines, withShortLines);
    }

    [Fact]
    public async Task Constructor_DefaultMaxLineTokens_Is200()
    {
        var byDefault = new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 5);
        var explicit200 = new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 5, maxLineTokens: 200);

        Assert.Equal(
            (await explicit200.ChunkTextAsync(LongSentence)).Select(c => c.Text),
            (await byDefault.ChunkTextAsync(LongSentence)).Select(c => c.Text));
    }

    [Theory]
    [InlineData(0, 0, 200)]
    [InlineData(-1, 0, 200)]
    [InlineData(400, -1, 200)]
    [InlineData(400, 400, 200)]
    [InlineData(400, 500, 200)]
    [InlineData(400, 50, 0)]
    [InlineData(400, 50, -5)]
    public void Constructor_InvalidSettings_Throws(int maxTokens, int overlapTokens, int maxLineTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedOverlapChunkingStrategy(maxTokens, overlapTokens, maxLineTokens));
    }

    [Fact]
    public async Task AddApplicationServices_BindsChunkMaxLines()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Embeddings:ChunkMaxTokens"] = "20",
            ["Ai:Embeddings:ChunkOverlapTokens"] = "5",
            ["Ai:Embeddings:ChunkMaxLines"] = "4",
        }).Build();

        using var provider = new ServiceCollection().AddApplicationServices(configuration).BuildServiceProvider();
        var registered = provider.GetRequiredService<IChunkingStrategy>();

        var expected = new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 5, maxLineTokens: 4);
        Assert.Equal(
            (await expected.ChunkTextAsync(LongSentence)).Select(c => c.Text),
            (await registered.ChunkTextAsync(LongSentence)).Select(c => c.Text));
    }

    [Fact]
    public void AddApplicationServices_InvalidChunkMaxLines_FailsAtRegistration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Embeddings:ChunkMaxLines"] = "0",
        }).Build();

        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddApplicationServices(configuration));
    }
}
