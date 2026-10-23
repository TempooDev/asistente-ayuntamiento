using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// BOJA hierarchical ingestion from the scraper's JSON document
/// (<c>{ document_id, metadata: { titulo, departamento, fecha_publicacion }, text }</c>).
/// </summary>
public class BojaIngestionService : BaseHierarchicalIngestionProcessor
{
    private readonly IBlobStorage _blobStorage;
    private readonly IChunkingStrategy _chunking;

    // Both extraction steps need the same document: download it once per (scoped) service instance.
    private (string BlobPath, ScrapedDocument? Document)? _cached;

    public BojaIngestionService(
        IBlobStorage blobStorage,
        IChunkingStrategy chunking,
        IAppDbContext dbContext,
        IFragmentEnrichmentService enrichmentService,
        IIngestionMetricsService metricsService,
        IVectorStore vectorStore,
        ILogger<BojaIngestionService> logger,
        Kernel kernel)
        : base(dbContext, enrichmentService, metricsService, vectorStore, logger, kernel)
    {
        _blobStorage = blobStorage;
        _chunking = chunking;
    }

    protected override BulletinType Bulletin => BulletinType.BOJA;

    protected override async Task<ParentDocument?> ExtractParentDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken)
    {
        var scraped = await LoadAsync(blobPath, cancellationToken);
        var metadata = scraped?.Metadata;

        // The scraper writes the bulletin date; documents stored before audit phase 6 may lack it.
        if (!PublicationDateParser.TryParse(metadata?.PublicationDate, out var publicationDate))
        {
            _logger.LogWarning("BOJA document {DocumentId} has no parsable publication date ({Value}); using ingestion time",
                documentId, metadata?.PublicationDate);
            publicationDate = DateTime.UtcNow;
        }

        return new ParentDocument
        {
            Bulletin = BulletinType.BOJA,
            DocumentId = documentId,
            NormTitle = string.IsNullOrWhiteSpace(metadata?.Title) ? documentId : metadata.Title,
            IssuingBody = string.IsNullOrWhiteSpace(metadata?.Department) ? "Junta de Andalucía" : metadata.Department,
            Municipality = MunicipalityExtractor.Extract(metadata?.Title, metadata?.Department),
            FullText = scraped?.Content ?? string.Empty,
            PublicationDate = publicationDate,
            IsActive = true,
            Metadata = "{}"
        };
    }

    protected override async Task<IEnumerable<RawFragment>> ExtractFragmentsAsync(string blobPath, string documentId, ParentDocument parentDoc, CancellationToken cancellationToken)
    {
        var scraped = await LoadAsync(blobPath, cancellationToken);
        var rawText = scraped?.Content ?? string.Empty;

        // BOJA pages carry no article structure: the whole text is split with the configured strategy, which cuts
        // on line and paragraph boundaries with overlap instead of every 1000 characters mid-word.
        return (await _chunking.ChunkTextAsync(rawText, cancellationToken))
            .Select(chunk => new RawFragment
            {
                Section = $"Sección {chunk.ChunkIndex + 1}",
                OriginalText = chunk.Text
            })
            .ToList();
    }

    private async Task<ScrapedDocument?> LoadAsync(string blobPath, CancellationToken cancellationToken)
    {
        if (_cached is { } cached && cached.BlobPath == blobPath)
        {
            return cached.Document;
        }

        await using var stream = await _blobStorage.OpenReadAsync(blobPath, cancellationToken);
        var document = await JsonSerializer.DeserializeAsync<ScrapedDocument>(stream, cancellationToken: cancellationToken);
        _cached = (blobPath, document);
        return document;
    }
}
