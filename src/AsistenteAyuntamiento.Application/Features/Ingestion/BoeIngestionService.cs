using AsistenteAyuntamiento.Domain.Common.Enums;
using System.Text.Json;
using System.Xml.Linq;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using AsistenteAyuntamiento.Application.Common.VectorStore;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// BOE hierarchical ingestion. Metadata (title, department, publication date) comes from the scraper's
/// JSON document (<c>blobPath</c>); the article structure comes from the raw BOE XML.
/// </summary>
public class BoeIngestionService : BaseHierarchicalIngestionProcessor
{
    private readonly IBlobStorage _blobStorage;

    public BoeIngestionService(
        IBlobStorage blobStorage,
        IAppDbContext dbContext,
        IFragmentEnrichmentService enrichmentService,
        IIngestionMetricsService metricsService,
        IVectorStore vectorStore,
        ILogger<BoeIngestionService> logger,
        Kernel kernel)
        : base(dbContext, enrichmentService, metricsService, vectorStore, logger, kernel)
    {
        _blobStorage = blobStorage;
    }

    protected override BulletinType Bulletin => BulletinType.BOE;

    protected override async Task<ParentDocument?> ExtractParentDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken)
    {
        var scraped = await LoadScrapedDocumentAsync(blobPath, cancellationToken);
        var metadata = scraped?.Metadata;

        if (!PublicationDateParser.TryParse(metadata?.PublicationDate, out var publicationDate))
        {
            _logger.LogWarning("BOE document {DocumentId} has no parsable publication date ({Value}); using ingestion time",
                documentId, metadata?.PublicationDate);
            publicationDate = DateTime.UtcNow;
        }

        return new ParentDocument
        {
            Bulletin = BulletinType.BOE,
            DocumentId = documentId,
            NormTitle = string.IsNullOrWhiteSpace(metadata?.Title) ? documentId : metadata.Title,
            IssuingBody = metadata?.Department,
            FullText = scraped?.Content ?? string.Empty,
            PublicationDate = publicationDate,
            IsActive = true,
            Metadata = "{}"
        };
    }

    protected override async Task<IEnumerable<RawFragment>> ExtractFragmentsAsync(string blobPath, string documentId, ParentDocument parentDoc, CancellationToken cancellationToken)
    {
        var xmlBlobPath = $"raw-xml/BOE/{documentId}.xml";
        await using var stream = await _blobStorage.OpenReadAsync(xmlBlobPath, cancellationToken);
        var xDoc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

        var articulos = xDoc.Descendants("articulo").ToList();
        if (!articulos.Any())
        {
            articulos.Add(new XElement("articulo", new XAttribute("id", "1"), xDoc.Root?.Element("texto")?.Value ?? parentDoc.FullText));
        }

        var result = new List<RawFragment>();

        foreach (var articulo in articulos)
        {
            var normSection = articulo.Attribute("id")?.Value ?? "Artículo Único";
            var originalText = articulo.Value;

            if (string.IsNullOrWhiteSpace(originalText)) continue;

            var lines = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextLines(originalText, 200);
            var paragraphs = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextParagraphs(lines, 400, 50);

            for (int i = 0; i < paragraphs.Count; i++)
            {
                var paragraph = paragraphs[i];
                var currentSection = paragraphs.Count > 1 ? $"{normSection} (parte {i + 1})" : normSection;

                result.Add(new RawFragment
                {
                    Section = currentSection,
                    ElementType = "General",
                    OriginalText = paragraph
                });
            }
        }

        return result;
    }

    private async Task<ScrapedDocument?> LoadScrapedDocumentAsync(string blobPath, CancellationToken cancellationToken)
    {
        await using var stream = await _blobStorage.OpenReadAsync(blobPath, cancellationToken);
        return await JsonSerializer.DeserializeAsync<ScrapedDocument>(stream, cancellationToken: cancellationToken);
    }
}
