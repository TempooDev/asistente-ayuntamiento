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
    private readonly Dictionary<string, string> _titles = [];
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

    private async Task IngestAsync(string documentId, string text, FlatChunkVariant? variant = null)
    {
        _texts[documentId] = text;
        variant ??= FlatChunkVariant.Baseline;
        await using var db = NewContext();
        IChunkingStrategy strategy = variant == FlatChunkVariant.Recursive
            ? new RecursiveChunkingStrategy(maxTokens: 20, overlapTokens: 0)
            : new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 0);
        var sut = new DocumentIngestionService(
            new JsonBlobs(_texts, _titles),
            db,
            KernelWithFakeEmbeddings(),
            _vectors,
            NullLogger<DocumentIngestionService>.Instance,
            strategy,
            Mock.Of<INotificationService>(), Mock.Of<IIngestionMetricsService>(), variant);
        await sut.ProcessBlobAsync($"json/BOE/{documentId}.json", "BOE");
    }

    private async Task<HashSet<ulong>> PointIdsAsync(string documentId, VectorCollection collection = VectorCollection.DocumentChunks)
    {
        var page = await _client.ScrollAsync(_options.NameOf(collection), MatchKeyword(VectorPayloadKeys.DocumentId, documentId), limit: 1000);
        return page.Result.Select(p => p.Id.Num).ToHashSet();
    }

    private async Task<HashSet<ulong>> ChunkIdsAsync(string documentId, string strategy = ChunkingStrategies.FixedOverlap)
    {
        await using var db = NewContext();
        return (await db.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == documentId && c.ChunkingStrategy == strategy).Select(c => c.Id).ToListAsync())
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
    public async Task A_study_variant_replaces_only_its_own_chunks_and_collection()
    {
        await IngestAsync(DocumentA, Paragraphs(4, "base"));
        var baselineChunks = await ChunkIdsAsync(DocumentA);
        var baselinePoints = await PointIdsAsync(DocumentA);

        await IngestAsync(DocumentA, Paragraphs(4, "primera"), FlatChunkVariant.Recursive);
        await IngestAsync(DocumentA, Paragraphs(2, "segunda"), FlatChunkVariant.Recursive);

        (await ChunkIdsAsync(DocumentA)).Should().BeEquivalentTo(baselineChunks, "the FixedOverlap rows belong to the baseline");
        (await PointIdsAsync(DocumentA)).Should().BeEquivalentTo(baselinePoints);
        var recursiveChunks = await ChunkIdsAsync(DocumentA, ChunkingStrategies.Recursive);
        recursiveChunks.Should().NotBeEmpty().And.NotIntersectWith(baselineChunks);
        (await PointIdsAsync(DocumentA, VectorCollection.RecursiveChunks)).Should().BeEquivalentTo(recursiveChunks, "the first recursive run is replaced");
        (await _client.CollectionExistsAsync(_options.RecursiveChunksCollection)).Should().BeTrue("the collection is created on the first write");
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
    public async Task A_municipality_filter_matches_its_documents_and_keeps_those_without_municipality_in_both_searches()
    {
        // BOJA names the issuing Ayuntamiento in the title; the other document has none and applies to every municipality.
        await IngestBojaAsync("BOJA-2026-10-2", Paragraphs(3, "subvencion"), title: "Anuncio de 3 de febrero de 2026, del Ayuntamiento de Sedella, de subvenciones.");
        await IngestBojaAsync("BOJA-2026-10-4", Paragraphs(3, "subvencion"));

        await using var db = NewContext();
        (await db.ParentDocuments.AsNoTracking().SingleAsync(p => p.DocumentId == "BOJA-2026-10-2")).Municipality.Should().Be("Sedella");
        var sedella = await FragmentIdsAsync(db, "BOJA-2026-10-2");
        var untagged = await FragmentIdsAsync(db, "BOJA-2026-10-4");

        // The citizen's spelling does not need to match the stored case or accents.
        var (dense, sparse) = await SearchBothAsync(db, "Ayuntamiento de SÉDELLA");
        dense.Should().Contain(sedella).And.Contain(untagged);
        sparse.Should().Contain(id => sedella.Contains(id)).And.Contain(id => untagged.Contains(id));

        (dense, sparse) = await SearchBothAsync(db, "Ronda");
        dense.Should().Contain(untagged).And.NotContain(sedella);
        sparse.Should().NotBeEmpty().And.OnlyContain(id => untagged.Contains(id));
    }

    private static async Task<List<long>> FragmentIdsAsync(AppDbContext db, string documentId) =>
        await db.ChildFragments.AsNoTracking().Where(c => c.Parent.DocumentId == documentId).Select(c => c.Id).ToListAsync();

    private async Task<(List<long> Dense, List<long> Sparse)> SearchBothAsync(AppDbContext db, string municipality)
    {
        // As HybridRetrievalService: both searches receive the filter as a match key.
        var key = MunicipalityExtractor.ToMatchKey(municipality)!;
        var dense = await _vectors.SearchAsync(VectorCollection.ChildFragments,
            new VectorSearchRequest([0.1f, 0.2f, 0.3f], Limit: 100, TextFilter: (VectorPayloadKeys.Municipality, key)));
        var sparse = await new PostgresFullTextSearch(db).SearchChildFragmentsAsync("subvencion1", key, 100);
        return (dense.Select(h => h.Id).ToList(), sparse.ToList());
    }

    [DockerFact]
    public async Task A_vector_store_failure_after_the_commit_is_reported_as_a_vector_sync_failure()
    {
        var failing = new Mock<IVectorStore>();
        failing.Setup(v => v.EnsureCollectionAsync(It.IsAny<VectorCollection>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("qdrant unavailable"));

        var act = () => IngestBojaAsync("BOJA-2026-10-3", Paragraphs(2, "texto"), failing.Object);

        (await act.Should().ThrowAsync<VectorSyncFailedException>()).WithInnerException<InvalidOperationException>();
        await using var db = NewContext();
        (await db.ParentDocuments.CountAsync(p => p.DocumentId == "BOJA-2026-10-3")).Should().Be(1, "PostgreSQL is committed first");
    }

    private async Task IngestBojaAsync(string documentId, string text, IVectorStore? vectors = null, string title = "Ayudas")
    {
        _texts[documentId] = text;
        _titles[documentId] = title;
        await using var db = NewContext();
        var enrichment = new Mock<IFragmentEnrichmentService>();
        enrichment.Setup(e => e.EnrichFragmentAsync(It.IsAny<BulletinType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulletinType _, string _, string _, string _, string _, string original, CancellationToken _) => (original, 0, 0));
        var sut = new BojaIngestionService(
            new JsonBlobs(_texts, _titles),
            new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 0),
            db,
            enrichment.Object,
            Mock.Of<IIngestionMetricsService>(),
            vectors ?? _vectors,
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
    private sealed class JsonBlobs(IReadOnlyDictionary<string, string> texts, IReadOnlyDictionary<string, string> titles) : IBlobStorage
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
                metadata = new { titulo = titles.GetValueOrDefault(documentId, "Ayudas"), departamento = "Ministerio", fecha_publicacion = "20261001" }
            });
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        }
    }
}
