using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion.Chunking;

/// <summary>Semantic chunking with a fake embedding model (spec: chunking-embeddings, Semantic Chunking Strategy).</summary>
public class SemanticChunkingStrategyTests
{
    private static readonly string[] TaxSentences = Enumerable.Range(1, 6)
        .Select(i => $"La ordenanza fiscal regula la tasa número {i} por recogida de residuos urbanos.").ToArray();

    private static readonly string[] JobSentences = Enumerable.Range(1, 6)
        .Select(i => $"La convocatoria ofrece el puesto de empleo número {i} en la plantilla municipal.").ToArray();

    [Fact]
    public async Task A_topic_change_starts_a_new_chunk()
    {
        var embeddings = new TopicEmbeddings();
        var sut = new SemanticChunkingStrategy(embeddings, new SemanticChunkingOptions(minTokens: 10));

        var chunks = await sut.ChunkTextAsync(string.Join(" ", TaxSentences.Concat(JobSentences)));

        chunks.Select(c => c.Text).Should().Equal(string.Join(" ", TaxSentences), string.Join(" ", JobSentences));
        chunks.Should().OnlyContain(c => c.Metadata["Strategy"] == "Semantic");
        chunks[0].EmbeddedTokens.Should().BeGreaterThan(0, "the sentence windows count towards the document's embedded tokens");
        chunks[1].EmbeddedTokens.Should().BeNull("they are reported once per document");
        embeddings.Inputs.Should().HaveCount(12, "one window per sentence");
        embeddings.Inputs.Should().NotContain(text => text.StartsWith("Instruct:", StringComparison.Ordinal), "windows are documents, not queries");
        embeddings.Inputs[1].Should().Be(string.Join(" ", TaxSentences.Take(3)), "each sentence is embedded with one neighbour on each side");
    }

    [Fact]
    public async Task A_single_sentence_is_one_chunk_without_embedding_calls()
    {
        var embeddings = new TopicEmbeddings();
        var text = string.Join(", ", Enumerable.Repeat("una frase muy larga sobre la tasa de residuos", 20)) + ".";

        var chunks = await new SemanticChunkingStrategy(embeddings, new SemanticChunkingOptions()).ChunkTextAsync(text);

        chunks.Should().ContainSingle().Which.Text.Should().Be(text);
        embeddings.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task A_text_under_the_minimum_is_one_chunk_without_embedding_calls()
    {
        var embeddings = new TopicEmbeddings();

        var chunks = await new SemanticChunkingStrategy(embeddings, new SemanticChunkingOptions()).ChunkTextAsync(TaxSentences[0] + " " + JobSentences[0]);

        chunks.Should().ContainSingle().Which.Text.Should().Be(TaxSentences[0] + " " + JobSentences[0]);
        embeddings.Inputs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_text_gives_no_chunks_and_no_embedding_calls(string text)
    {
        var embeddings = new TopicEmbeddings();

        (await new SemanticChunkingStrategy(embeddings, new SemanticChunkingOptions()).ChunkTextAsync(text)).Should().BeEmpty();
        embeddings.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task A_chunk_over_the_maximum_is_split_recursively()
    {
        // One topic: no breakpoint, so the whole text is one semantic chunk of about 120 tokens.
        var text = string.Join(" ", TaxSentences);
        var sut = new SemanticChunkingStrategy(new TopicEmbeddings(), new SemanticChunkingOptions(minTokens: 5, chunkMaxTokens: 40));

        var chunks = await sut.ChunkTextAsync(text);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(c => ChunkTokenCounter.Count(c.Text) <= 40);
        string.Join(" ", chunks.Select(c => c.Text)).Should().Be(text, "the recursive split adds no overlap");
        chunks.Select(c => c.ChunkIndex).Should().Equal(Enumerable.Range(0, chunks.Count));
    }

    [Fact]
    public async Task A_long_table_without_full_stops_never_sends_an_oversized_window_to_the_embedding_model()
    {
        // A table flattened to text: thousands of tokens and no sentence end, so the splitter sees one "sentence".
        var table = string.Join("\n", Enumerable.Range(1, 400).Select(i => $"| Partida {i} | Tasa de residuos | {i * 10},00 euros |"));
        ChunkTokenCounter.Count(table).Should().BeGreaterThan(40 * 4 * 10);
        var embeddings = new TopicEmbeddings();
        var sut = new SemanticChunkingStrategy(embeddings, new SemanticChunkingOptions(bufferSize: 3, minTokens: 5, chunkMaxTokens: 40));

        var chunks = await sut.ChunkTextAsync(table);

        embeddings.Inputs.Should().HaveCountGreaterThan(1, "the oversized sentence is split before embedding");
        embeddings.Inputs.Should().OnlyContain(window => ChunkTokenCounter.Count(window) <= 40 * SemanticChunkingStrategy.WindowMaxChunks);
        chunks.Should().OnlyContain(c => ChunkTokenCounter.Count(c.Text) <= 40);
    }

    [Fact]
    public void A_window_takes_fewer_neighbours_when_they_would_exceed_the_budget()
    {
        string[] sentences = [new string('a', 40), new string('b', 40), new string('c', 400), new string('d', 40)];

        // Within 30 tokens, "b" (10 tokens) takes "a" (10) but not "c" (100); "c" keeps only itself.
        var windows = SemanticChunkingStrategy.Windows(sentences, buffer: 1, maxTokens: 30);

        windows[1].Should().Be(sentences[0] + " " + sentences[1]);
        windows[2].Should().Be(sentences[2]);
        SemanticChunkingStrategy.Windows(sentences, buffer: 1)[1].Should().Be(string.Join(" ", sentences.Take(3)), "no budget means the full buffer");
    }

    [Fact]
    public async Task A_chunk_under_the_minimum_is_merged_with_the_next()
    {
        // The breakpoint leaves one tax sentence alone; it is too small to be a chunk of its own.
        var text = TaxSentences[0] + " " + string.Join(" ", JobSentences);
        var sut = new SemanticChunkingStrategy(new TopicEmbeddings(), new SemanticChunkingOptions(bufferSize: 0, breakpointPercentile: 90, minTokens: 30));

        var chunks = await sut.ChunkTextAsync(text);

        chunks.Should().ContainSingle().Which.Text.Should().Be(text);
    }

    [Fact]
    public async Task Sentence_windows_are_embedded_in_batches_of_one_hundred()
    {
        var embeddings = new TopicEmbeddings();
        var text = string.Join(" ", Enumerable.Range(1, 150).Select(i => $"La tasa número {i} se abona en el plazo de un mes desde la notificación."));

        await new SemanticChunkingStrategy(embeddings, new SemanticChunkingOptions()).ChunkTextAsync(text);

        embeddings.BatchSizes.Should().Equal(100, 50);
    }

    [Fact]
    public async Task An_embedding_failure_is_thrown()
    {
        var sut = new SemanticChunkingStrategy(new TopicEmbeddings(fail: true), new SemanticChunkingOptions(minTokens: 10));

        var act = () => sut.ChunkTextAsync(string.Join(" ", TaxSentences.Concat(JobSentences)));

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData(-1, 95, 100, 400)]
    [InlineData(1, 0, 100, 400)]
    [InlineData(1, 101, 100, 400)]
    [InlineData(1, 95, -1, 400)]
    [InlineData(1, 95, 400, 400)]
    [InlineData(1, 95, 100, 0)]
    public void Invalid_settings_throw(int bufferSize, double percentile, int minTokens, int maxTokens) =>
        FluentActions.Invoking(() => new SemanticChunkingOptions(bufferSize, percentile, minTokens, maxTokens)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void The_percentile_interpolates_between_ranks()
    {
        SemanticChunkingStrategy.Percentile([0, 0, 0, 0.1, 0.2], 95).Should().BeApproximately(0.18, 1e-9);
        SemanticChunkingStrategy.Percentile([0.3], 95).Should().Be(0.3);
    }

    /// <summary>Two dimensions: how much a window talks about the tax ordinance and how much about the job offer.</summary>
    private sealed class TopicEmbeddings(bool fail = false) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<string> Inputs { get; } = [];
        public List<int> BatchSizes { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (fail)
            {
                throw new HttpRequestException("ollama down");
            }

            var batch = values.ToList();
            Inputs.AddRange(batch);
            BatchSizes.Add(batch.Count);
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(batch.Select(text => new Embedding<float>(new[]
            {
                Count(text, "tasa") + 0.01f,
                Count(text, "empleo") + 0.01f,
            }))));
        }

        private static float Count(string text, string word) =>
            (text.Length - text.Replace(word, string.Empty, StringComparison.Ordinal).Length) / (float)word.Length;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
