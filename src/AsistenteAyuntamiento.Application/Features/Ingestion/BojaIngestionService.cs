using AsistenteAyuntamiento.Domain.Common.Enums;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Qdrant.Client;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// BOJA hierarchical ingestion from the scraper's JSON document
/// (<c>{ document_id, metadata: { titulo, departamento, fecha_publicacion }, text }</c>).
/// </summary>
public class BojaIngestionService : BaseHierarchicalIngestionProcessor
{
    private const int ChunkSize = 1000;

    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    // Both extraction steps need the same document: download it once per (scoped) service instance.
    private (string BlobPath, ScrapedDocument? Document)? _cached;

    public BojaIngestionService(
        IAmazonS3 s3Client,
        IAppDbContext dbContext,
        IFragmentEnrichmentService enrichmentService,
        IIngestionMetricsService metricsService,
        QdrantClient qdrantClient,
        ILogger<BojaIngestionService> logger,
        Kernel kernel,
        IConfiguration configuration)
        : base(dbContext, enrichmentService, metricsService, qdrantClient, logger, kernel)
    {
        _s3Client = s3Client;
        _bucketName = configuration["Blob:BucketName"] ?? AsistenteAyuntamiento.Shared.AppConstants.BlobStorage.DefaultBucketName;
    }

    protected override BulletinType Bulletin => BulletinType.BOJA;

    protected override async Task<ParentDocument?> ExtractParentDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken)
    {
        var scraped = await LoadAsync(blobPath, cancellationToken);
        var metadata = scraped?.Metadata;

        // TODO(audit-remediation 6.3): the Go BOJA client still writes the scrape date instead of the bulletin date.
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

        // TODO(audit-remediation 6.x / M2): use IChunkingStrategy instead of fixed-size slices.
        var result = new List<RawFragment>();

        for (int i = 0; i < rawText.Length; i += ChunkSize)
        {
            var originalText = rawText.Substring(i, Math.Min(ChunkSize, rawText.Length - i));
            var normSection = $"Sección {i / ChunkSize + 1}";

            result.Add(new RawFragment
            {
                Section = normSection,
                ElementType = "Párrafo",
                OriginalText = originalText
            });
        }

        return result;
    }

    private async Task<ScrapedDocument?> LoadAsync(string blobPath, CancellationToken cancellationToken)
    {
        if (_cached is { } cached && cached.BlobPath == blobPath)
        {
            return cached.Document;
        }

        using var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = blobPath }, cancellationToken);
        var document = await JsonSerializer.DeserializeAsync<ScrapedDocument>(response.ResponseStream, cancellationToken: cancellationToken);
        _cached = (blobPath, document);
        return document;
    }
}
