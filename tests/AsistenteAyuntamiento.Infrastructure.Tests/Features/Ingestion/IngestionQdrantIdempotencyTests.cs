using System.Text;
using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Data.Search;
using AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;
using AsistenteAyuntamiento.Infrastructure.VectorStore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Moq;
using Qdrant.Client;
using Testcontainers.PostgreSql;
using Testcontainers.Qdrant;
using static Qdrant.Client.Grpc.Conditions;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ingestion;

/// <summary>
/// Re-ingestion against a real PostgreSQL and a real Qdrant (spec: ingestion-consistency): the document's vectors
/// end up matching its chunks exactly, with no orphans, and other documents are untouched.
/// </summary>
public sealed class IngestionQdrantIdempotencyTests : IAsyncLifetime
{
    private const string DocumentA = "BOE-A-2026-10";
    private const string DocumentB = "BOE-A-2026-20";

    private PostgreSqlContainer? _postgres;
    private QdrantContainer? _qdrant;
    private QdrantClient _client = null!;
    private QdrantVectorStore _vectors = null!;
    private readonly Dictionary<string, string> _texts = [];
    private readonly VectorStoreOptions _options = new();

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable) return;
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        _qdrant = new QdrantBuilder("qdrant/qdrant:v1.19.2").Build();
        await Task.WhenAll(_postgres.StartAsync(), _qdrant.StartAsync());

        await using var db = NewContext();
        await db.Database.MigrateAsync();
        _client = new QdrantClient(new Uri(_qdrant.GetGrpcConnectionString()));
        _vectors = new QdrantVectorStore(_client, Options.Create(_options), NullLogger<QdrantVectorStore>.Instance);
    }

    public async Task DisposeAsync()
    {
        _vectors?.Dispose();
        _client?.Dispose();
        if (_qdrant is not null) await _qdrant.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres!.GetConnectionString(), o => o.UseVector()).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == "system"));

    private async Task IngestAsync(string documentId, string text)
    {
        _texts[documentId] = text;
        await using var db = NewContext();
        var sut = new DocumentIngestionService(
            new JsonBlobs(_texts),
            db,
            KernelWithFakeEmbeddings(),
            _vectors,
            NullLogger<DocumentIngestionService>.Instance,
            new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 0),
            Mock.Of<INotificationService>());
        await sut.ProcessBlobAsync($"json/BOE/{documentId}.json", "BOE");
    }

    private async Task<HashSet<ulong>> PointIdsAsync(string documentId)
    {
        var page = await _client.ScrollAsync(_options.DocumentChunksCollection, MatchKeyword(VectorPayloadKeys.DocumentId, documentId), limit: 1000);
        return page.Result.Select(p => p.Id.Num).ToHashSet();
    }

    private async Task<HashSet<ulong>> ChunkIdsAsync(string documentId)
    {
        await using var db = NewContext();
        return (await db.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == documentId).Select(c => c.Id).ToListAsync())
            .Select(id => (ulong)id).ToHashSet();
    }

    private static string Paragraphs(int count, string word) =>
        string.Join("\n\n", Enumerable.Range(1, count).Select(i => string.Join(' ', Enumerable.Repeat($"{word}{i}", 25))));

    [DockerFact]
    public async Task Re_ingesting_leaves_exactly_the_new_chunks_in_qdrant_and_keeps_other_documents()
    {
        await IngestAsync(DocumentA, Paragraphs(6, "primera"));
        await IngestAsync(DocumentB, Paragraphs(3, "otra"));
        var documentBPoints = await PointIdsAsync(DocumentB);
        (await PointIdsAsync(DocumentA)).Should().HaveCount((await ChunkIdsAsync(DocumentA)).Count).And.HaveCountGreaterThan(2);

        await IngestAsync(DocumentA, Paragraphs(2, "segunda"));

        var chunks = await ChunkIdsAsync(DocumentA);
        (await PointIdsAsync(DocumentA)).Should().BeEquivalentTo(chunks, "the old vectors are deleted, not left as orphans");
        (await PointIdsAsync(DocumentB)).Should().BeEquivalentTo(documentBPoints);
        (await _client.CountAsync(_options.DocumentChunksCollection, exact: true))
            .Should().Be((ulong)(chunks.Count + documentBPoints.Count));
    }

    [DockerFact]
    public async Task Ingesting_the_same_text_twice_is_a_no_op_for_the_number_of_vectors()
    {
        await IngestAsync(DocumentA, Paragraphs(4, "igual"));
        var first = await _client.CountAsync(_options.DocumentChunksCollection, exact: true);

        await IngestAsync(DocumentA, Paragraphs(4, "igual"));

        (await _client.CountAsync(_options.DocumentChunksCollection, exact: true)).Should().Be(first);
        (await PointIdsAsync(DocumentA)).Should().BeEquivalentTo(await ChunkIdsAsync(DocumentA));
    }

    [DockerFact]
    public async Task Re_ingesting_a_hierarchical_document_replaces_its_child_fragment_vectors()
    {
        const string boja = "BOJA-2026-10-1";
        await IngestBojaAsync(boja, Paragraphs(6, "primera"));
        await IngestBojaAsync(boja, Paragraphs(2, "segunda"));

        await using var db = NewContext();
        var parents = await db.ParentDocuments.AsNoTracking().Where(p => p.DocumentId == boja).Select(p => p.Id).ToListAsync();
        parents.Should().ContainSingle("the previous version of the parent document is replaced, not duplicated");
        var children = (await db.ChildFragments.AsNoTracking().Where(c => c.ParentId == parents[0]).Select(c => c.Id).ToListAsync())
            .Select(id => (ulong)id).ToHashSet();

        var page = await _client.ScrollAsync(_options.ChildFragmentsCollection, limit: 1000);
        page.Result.Select(p => p.Id.Num).Should().BeEquivalentTo(children, "no vectors of the first version are left behind");
    }

    [DockerFact]
    public async Task A_municipality_filter_keeps_fragments_without_municipality_in_both_searches()
    {
        // Bulletins are not tagged with a municipality: a question about one must still find them.
        await IngestBojaAsync("BOJA-2026-10-2", Paragraphs(3, "subvencion"));

        var dense = await _vectors.SearchAsync(VectorCollection.ChildFragments,
            new VectorSearchRequest([0.1f, 0.2f, 0.3f], Limit: 10, TextFilter: (VectorPayloadKeys.Municipality, "Sedella")));
        dense.Should().NotBeEmpty();

        await using var db = NewContext();
        var sparse = await new PostgresFullTextSearch(db).SearchChildFragmentsAsync("subvencion1", "Sedella", 10);
        sparse.Should().NotBeEmpty();
    }

    private async Task IngestBojaAsync(string documentId, string text)
    {
        _texts[documentId] = text;
        await using var db = NewContext();
        var enrichment = new Mock<IFragmentEnrichmentService>();
        enrichment.Setup(e => e.EnrichFragmentAsync(It.IsAny<BulletinType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulletinType _, string _, string _, string _, string _, string original, CancellationToken _) => (original, 0, 0));
        var sut = new BojaIngestionService(
            new JsonBlobs(_texts),
            new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 0),
            db,
            enrichment.Object,
            Mock.Of<IIngestionMetricsService>(),
            _vectors,
            NullLogger<BojaIngestionService>.Instance,
            KernelWithFakeEmbeddings());
        await sut.ProcessDocumentAsync($"json/BOJA/{documentId}.json", documentId, CancellationToken.None);
    }

    private static Kernel KernelWithFakeEmbeddings()
    {
        var generator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> values, EmbeddingGenerationOptions? _, CancellationToken _) =>
                new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f }))));
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(generator.Object);
        return builder.Build();
    }

    /// <summary>Scraper JSON per document id, taken from the blob key (<c>json/BOE/{id}.json</c>).</summary>
    private sealed class JsonBlobs(IReadOnlyDictionary<string, string> texts) : IBlobStorage
    {
        public Task EnsureContainerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IAsyncEnumerable<BlobObjectInfo> ListAsync(string prefix, CancellationToken cancellationToken = default) => AsyncEnumerable.Empty<BlobObjectInfo>();
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            var documentId = Path.GetFileNameWithoutExtension(key);
            var json = JsonSerializer.Serialize(new
            {
                document_id = documentId,
                text = texts[documentId],
                metadata = new { titulo = "Ayudas", departamento = "Ministerio", fecha_publicacion = "20261001" }
            });
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        }
    }
}
