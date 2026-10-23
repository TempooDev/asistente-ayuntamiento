using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion.Chunking;

/// <summary>Recursive splitting (spec: chunking-embeddings, Recursive Chunking Strategy).</summary>
public class RecursiveChunkingStrategyTests
{
    /// <summary>About <paramref name="tokens"/> tokens of words, without line breaks or sentence ends.</summary>
    private static string Words(int tokens, string word = "palabra") =>
        string.Join(" ", Enumerable.Repeat(word, tokens * 4 / (word.Length + 1)));

    [Fact]
    public async Task Paragraphs_that_fit_are_kept_whole()
    {
        var paragraphs = new[] { Words(150, "uno"), Words(150, "dos"), Words(150, "tres") };

        var chunks = await new RecursiveChunkingStrategy(400, 50).ChunkTextAsync(string.Join("\n\n", paragraphs));

        chunks.Select(c => c.Text).Should().Equal(paragraphs[0] + "\n\n" + paragraphs[1], paragraphs[2]);
        chunks.Select(c => c.ChunkIndex).Should().Equal(0, 1);
        chunks.Should().OnlyContain(c => c.Metadata["Strategy"] == "Recursive" && c.EmbeddedTokens == null);
    }

    [Fact]
    public async Task A_long_paragraph_is_split_at_lines_and_sentences_within_the_limit()
    {
        var lines = Enumerable.Range(1, 8).Select(i => string.Join(". ", Enumerable.Range(1, 6).Select(j => $"Frase {i}.{j} " + Words(20, "texto"))) + ".");
        var paragraph = string.Join("\n", lines);
        ChunkTokenCounter.Count(paragraph).Should().BeGreaterThan(800);

        var chunks = await new RecursiveChunkingStrategy(400, 50).ChunkTextAsync(paragraph);

        chunks.Should().HaveCountGreaterThan(2);
        chunks.Should().OnlyContain(c => ChunkTokenCounter.Count(c.Text) <= 400);
        chunks.Should().OnlyContain(c => paragraph.Contains(c.Text), "a chunk is a contiguous span of the text");
        chunks.Should().OnlyContain(c => c.Text.StartsWith("Frase", StringComparison.Ordinal), "cuts fall at line or sentence boundaries");
    }

    [Fact]
    public async Task Text_without_separators_is_cut_by_characters()
    {
        var text = new string('x', 4000);

        var chunks = await new RecursiveChunkingStrategy(400, 50).ChunkTextAsync(text);

        chunks.Should().HaveCount(3);
        chunks.Should().OnlyContain(c => ChunkTokenCounter.Count(c.Text) <= 400);
        string.Concat(chunks.Select(c => c.Text)).Should().Be(text);
    }

    [Fact]
    public async Task Consecutive_chunks_share_whole_pieces_up_to_the_overlap()
    {
        var sentences = Enumerable.Range(1, 12).Select(i => $"Frase número {i:00} del texto.").ToList();
        var text = string.Join(" ", sentences);

        var chunks = await new RecursiveChunkingStrategy(maxTokens: 30, overlapTokens: 8).ChunkTextAsync(text);

        chunks.Should().HaveCountGreaterThan(2);
        for (var i = 1; i < chunks.Count; i++)
        {
            var lastSentenceOfPrevious = sentences.Last(s => chunks[i - 1].Text.Contains(s));
            chunks[i].Text.Should().StartWith(lastSentenceOfPrevious, "the overlap is the last whole sentence of the previous chunk");
        }
        chunks.Should().OnlyContain(c => ChunkTokenCounter.Count(c.Text) <= 30);
    }

    [Fact]
    public async Task Without_overlap_chunks_do_not_repeat_text()
    {
        var text = string.Join(" ", Enumerable.Range(1, 12).Select(i => $"Frase número {i:00} del texto."));

        var chunks = await new RecursiveChunkingStrategy(maxTokens: 30, overlapTokens: 0).ChunkTextAsync(text);

        string.Join(" ", chunks.Select(c => c.Text)).Should().Be(text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n ")]
    public async Task Empty_text_gives_no_chunks(string text) =>
        (await new RecursiveChunkingStrategy().ChunkTextAsync(text)).Should().BeEmpty();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(400, -1)]
    [InlineData(400, 400)]
    public void Invalid_limits_throw(int maxTokens, int overlapTokens) =>
        FluentActions.Invoking(() => new RecursiveChunkingStrategy(maxTokens, overlapTokens)).Should().Throw<ArgumentOutOfRangeException>();
}
