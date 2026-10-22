using System.Text;
using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Moq;
using Testcontainers.PostgreSql;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ingestion;

/// <summary>
/// Re-ingesting a document replaces its chunks and vectors (spec: ingestion-consistency). Runs on a real
/// PostgreSQL with the full migration history; blob storage, embeddings and the vector store are fakes.
/// </summary>
public sealed class BaselineIngestionIdempotencyTests : IAsyncLifetime
{
    private const string BlobPath = "json/BOE/BOE-A-2026-1.json";
    private PostgreSqlContainer? _postgres;
    private readonly RecordingVectorStore _vectors = new();
    private string _documentText = string.Empty;

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable) return;
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres!.GetConnectionString(), o => o.UseVector()).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == "system"));

    private async Task IngestAsync(string text, Func<string, string>? json = null)
    {
        _documentText = text;
        await using var db = NewContext();
        var sut = new DocumentIngestionService(
            new JsonBlob(() => _documentText, json),
            db,
            KernelWithFakeEmbeddings(),
            _vectors,
            NullLogger<DocumentIngestionService>.Instance,
            new FixedOverlapChunkingStrategy(maxTokens: 20, overlapTokens: 0),
            Mock.Of<INotificationService>(), Mock.Of<IIngestionMetricsService>());
        await sut.ProcessBlobAsync(BlobPath, "BOE");
    }

    private static string Paragraphs(int count, string word) =>
        string.Join("\n\n", Enumerable.Range(1, count).Select(i => string.Join(' ', Enumerable.Repeat($"{word}{i}", 25))));

    [DockerFact]
    public async Task Re_ingesting_replaces_chunks_and_vectors_instead_of_adding_them()
    {
        await IngestAsync(Paragraphs(6, "primera"));
        await IngestAsync(Paragraphs(2, "segunda"));

        await using var db = NewContext();
        var chunks = await db.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == "BOE-A-2026-1").ToListAsync();
        chunks.Should().NotBeEmpty().And.OnlyContain(c => c.Content.Contains("segunda"));

        // Each run deletes the document's vectors before upserting the new ones, which match the stored chunks.
        _vectors.Operations.Should().Equal("ensure", "delete BOE-A-2026-1", "upsert", "ensure", "delete BOE-A-2026-1", "upsert");
        _vectors.LastUpsertIds.Should().BeEquivalentTo(chunks.Select(c => (long)c.Id));

        var job = await db.DocumentJobStates.SingleAsync(j => j.DocumentId == "BOE-A-2026-1");
        job.Status.Should().Be(JobStatus.Completed);
    }

    [DockerFact]
    public async Task A_vector_store_failure_after_the_commit_marks_the_job_for_resync()
    {
        _vectors.FailUpserts = true;

        var act = () => IngestAsync(Paragraphs(2, "texto"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        await using var db = NewContext();
        (await db.DocumentChunks.CountAsync()).Should().BeGreaterThan(0, "PostgreSQL is the source of truth and is committed first");
        (await db.DocumentJobStates.SingleAsync()).Status.Should().Be(JobStatus.VectorSyncFailed);
    }

    [DockerFact]
    public async Task A_blob_that_deserializes_to_null_ends_Failed_with_an_error_instead_of_Processing()
    {
        await IngestAsync("", json: _ => "null");

        await using var db = NewContext();
        var job = await db.DocumentJobStates.AsNoTracking().SingleAsync();
        job.DocumentId.Should().Be("BOE-A-2026-1");
        (job.Status, job.BaselineStatus).Should().Be((JobStatus.Failed, (JobStatus?)JobStatus.Failed));
        job.ErrorMessage.Should().StartWith("[Baseline]").And.Contain(BlobPath);
        (await db.DocumentChunks.CountAsync()).Should().Be(0);
        _vectors.Operations.Should().BeEmpty();
    }

    [DockerFact]
    public async Task Every_state_chunk_and_vector_uses_the_id_of_the_path()
    {
        // The JSON declares another id: the run must not leave a Processing row under one id and the result under another.
        await IngestAsync(Paragraphs(2, "texto"), json: text => JsonSerializer.Serialize(new { document_id = "otro-id", text }));

        await using var db = NewContext();
        var job = await db.DocumentJobStates.AsNoTracking().SingleAsync();
        (job.DocumentId, job.Status).Should().Be(("BOE-A-2026-1", JobStatus.Completed));
        (await db.DocumentChunks.Select(c => c.DocumentId).Distinct().ToListAsync()).Should().Equal("BOE-A-2026-1");
        _vectors.Operations.Should().Contain("delete BOE-A-2026-1");
    }

    [DockerFact]
    public async Task The_chunk_payload_carries_the_municipality_match_key_of_the_issuing_Ayuntamiento()
    {
        await IngestWithDepartmentAsync("AYUNTAMIENTO DE VÉLEZ-MÁLAGA");

        _vectors.LastUpsertPayloads.Should().NotBeEmpty()
            .And.OnlyContain(p => (string)p[VectorPayloadKeys.Municipality] == "velez malaga");
    }

    [DockerFact]
    public async Task A_state_norm_has_an_empty_municipality_in_the_chunk_payload()
    {
        await IngestWithDepartmentAsync("Ministerio de Vivienda y Agenda Urbana");

        _vectors.LastUpsertPayloads.Should().NotBeEmpty()
            .And.OnlyContain(p => (string)p[VectorPayloadKeys.Municipality] == "");
    }

    private Task IngestWithDepartmentAsync(string department) =>
        IngestAsync(Paragraphs(2, "texto"), json: text => JsonSerializer.Serialize(new
        {
            document_id = "BOE-A-2026-1",
            text,
            metadata = new { titulo = "Anuncio de licitación del servicio de limpieza viaria", departamento = department, fecha_publicacion = "20261001" }
        }));

    private static Kernel KernelWithFakeEmbeddings()
    {
        var generator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> values, EmbeddingGenerationOptions? _, CancellationToken _) =>
                new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(new float[] { 0.1f, 0.2f }))));
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(generator.Object);
        return builder.Build();
    }

    private sealed class JsonBlob(Func<string> text, Func<string, string>? json = null) : IBlobStorage
    {
        public Task EnsureContainerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IAsyncEnumerable<BlobObjectInfo> ListAsync(string prefix, CancellationToken cancellationToken = default) => AsyncEnumerable.Empty<BlobObjectInfo>();
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            var content = json?.Invoke(text()) ?? JsonSerializer.Serialize(new
            {
                document_id = "BOE-A-2026-1",
                text = text(),
                metadata = new { titulo = "Ayudas", departamento = "Ministerio", fecha_publicacion = "20261001" }
            });
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        }
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public List<string> Operations { get; } = [];
        public IReadOnlyList<long> LastUpsertIds { get; private set; } = [];
        public IReadOnlyList<IReadOnlyDictionary<string, object>> LastUpsertPayloads { get; private set; } = [];
        public bool FailUpserts { get; set; }

        public Task EnsureCollectionAsync(VectorCollection collection, int vectorSize, CancellationToken cancellationToken = default)
        {
            Operations.Add("ensure");
            return Task.CompletedTask;
        }

        public Task<bool> CollectionExistsAsync(VectorCollection collection, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DropCollectionAsync(VectorCollection collection, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertAsync(VectorCollection collection, IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default)
        {
            if (FailUpserts) throw new InvalidOperationException("qdrant unavailable");
            Operations.Add("upsert");
            LastUpsertIds = points.Select(p => p.Id).ToList();
            LastUpsertPayloads = points.Select(p => (IReadOnlyDictionary<string, object>)p.Payload).ToList();
            return Task.CompletedTask;
        }

        public Task DeleteDocumentAsync(VectorCollection collection, string documentId, IReadOnlyCollection<long>? parentIds = null, CancellationToken cancellationToken = default)
        {
            Operations.Add($"delete {documentId}");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorCollection collection, VectorSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VectorSearchHit>>([]);

        public Task<VectorCollectionStats?> GetStatsAsync(VectorCollection collection, CancellationToken cancellationToken = default) =>
            Task.FromResult<VectorCollectionStats?>(null);

        public Task<long> CountDocumentPointsAsync(VectorCollection collection, string documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);
    }
}
