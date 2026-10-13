using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion;

/// <summary>Only the bulletins in scope (BOE, BOJA) are ingested (spec: chunking-embeddings).</summary>
public class DocumentIngestionScopeTests
{
    [Theory]
    [InlineData("BOPMA")]
    [InlineData("DOUE")]
    public async Task Documents_from_other_sources_are_discarded_without_reading_them(string source)
    {
        var blobs = new Mock<IBlobStorage>(MockBehavior.Strict);
        var db = new Mock<IAppDbContext>(MockBehavior.Strict);
        var sut = new DocumentIngestionService(blobs.Object, db.Object, Kernel.CreateBuilder().Build(), Mock.Of<IVectorStore>(),
            NullLogger<DocumentIngestionService>.Instance, new FixedOverlapChunkingStrategy(), Mock.Of<INotificationService>(), Mock.Of<IIngestionMetricsService>());

        await sut.ProcessBlobAsync($"json/{source}/{source}-1.json", source);

        // Strict mocks: any blob read or database access would have thrown.
        blobs.VerifyNoOtherCalls();
        db.VerifyNoOtherCalls();
    }
}
