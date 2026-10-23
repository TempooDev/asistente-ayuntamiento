using System.Text.Json;
using System.Xml.Linq;
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
/// BOE hierarchical ingestion. Metadata (title, department, publication date) comes from the scraper's
/// JSON document (<c>blobPath</c>); the article structure comes from the raw BOE XML. The scraper stores that XML
/// only as a best-effort backup, so without it the JSON text is split as a single article.
/// </summary>
public class BoeIngestionService : BaseHierarchicalIngestionProcessor
{
    private readonly IBlobStorage _blobStorage;
    private readonly IChunkingStrategy _chunking;

    public BoeIngestionService(
        IBlobStorage blobStorage,
        IChunkingStrategy chunking,
        IAppDbContext dbContext,
        IFragmentEnrichmentService enrichmentService,
        IIngestionMetricsService metricsService,
        IVectorStore vectorStore,
        ILogger<BoeIngestionService> logger,
        Kernel kernel)
        : base(dbContext, enrichmentService, metricsService, vectorStore, logger, kernel)
    {
        _blobStorage = blobStorage;
        _chunking = chunking;
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
            Municipality = MunicipalityExtractor.Extract(metadata?.Title, metadata?.Department),
            FullText = scraped?.Content ?? string.Empty,
            PublicationDate = publicationDate,
            IsActive = true,
            Metadata = "{}"
        };
    }

    protected override async Task<IEnumerable<RawFragment>> ExtractFragmentsAsync(string blobPath, string documentId, ParentDocument parentDoc, CancellationToken cancellationToken)
    {
        var xDoc = await LoadRawXmlAsync(documentId, cancellationToken);

        var articulos = xDoc?.Descendants("articulo").ToList() ?? [];
        if (!articulos.Any())
        {
            articulos.Add(new XElement("articulo", new XAttribute("id", "1"), xDoc?.Root?.Element("texto")?.Value ?? parentDoc.FullText));
        }

        var result = new List<RawFragment>();

        foreach (var articulo in articulos)
        {
            var normSection = articulo.Attribute("id")?.Value ?? "Artículo Único";
            var originalText = articulo.Value;

            if (string.IsNullOrWhiteSpace(originalText)) continue;

            // Long articles are split with the configured strategy (Ai:Embeddings:ChunkMaxTokens / ChunkOverlapTokens).
            var paragraphs = (await _chunking.ChunkTextAsync(originalText, cancellationToken)).Select(c => c.Text).ToList();

            for (int i = 0; i < paragraphs.Count; i++)
            {
                result.Add(new RawFragment
                {
                    Section = normSection,
                    SubSection = paragraphs.Count > 1 ? $"parte {i + 1}" : null,
                    OriginalText = paragraphs[i]
                });
            }
        }

        return result;
    }

    /// <summary>The raw BOE XML, or <c>null</c> when the scraper could not store it (its backup is optional).</summary>
    private async Task<XDocument?> LoadRawXmlAsync(string documentId, CancellationToken cancellationToken)
    {
        var xmlBlobPath = $"raw-xml/BOE/{documentId}.xml";
        try
        {
            await using var stream = await _blobStorage.OpenReadAsync(xmlBlobPath, cancellationToken);
            return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        }
        catch (BlobNotFoundException)
        {
            _logger.LogWarning("BOE document {DocumentId} has no raw XML ({XmlBlobPath}); using the text of the JSON document", documentId, xmlBlobPath);
            return null;
        }
    }

    private async Task<ScrapedDocument?> LoadScrapedDocumentAsync(string blobPath, CancellationToken cancellationToken)
    {
        await using var stream = await _blobStorage.OpenReadAsync(blobPath, cancellationToken);
        return await JsonSerializer.DeserializeAsync<ScrapedDocument>(stream, cancellationToken: cancellationToken);
    }
}
