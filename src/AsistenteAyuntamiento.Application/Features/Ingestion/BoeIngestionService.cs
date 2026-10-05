using AsistenteAyuntamiento.Domain.Common.Enums;
using System.Xml.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Qdrant.Client;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class BoeIngestionService : BaseHierarchicalIngestionProcessor
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName = AsistenteAyuntamiento.Shared.AppConstants.BlobStorage.DefaultBucketName;

    public BoeIngestionService(
        IAmazonS3 s3Client,
        IAppDbContext dbContext,
        IFragmentEnrichmentService enrichmentService,
        IIngestionMetricsService metricsService,
        QdrantClient qdrantClient,
        ILogger<BoeIngestionService> logger,
        Kernel kernel) 
        : base(dbContext, enrichmentService, metricsService, qdrantClient, logger, kernel)
    {
        _s3Client = s3Client;
    }

    protected override BulletinType Bulletin => BulletinType.BOE;

    protected override async Task<ParentDocument?> ExtractParentDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken)
    {
        var xmlBlobPath = $"raw-xml/BOE/{documentId}.xml";
        using var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = xmlBlobPath }, cancellationToken);
        var xDoc = await XDocument.LoadAsync(response.ResponseStream, LoadOptions.None, cancellationToken);

        return new ParentDocument
        {
            Bulletin = BulletinType.BOE,
            DocumentId = documentId,
            NormTitle = xDoc.Root?.Element("titulo")?.Value ?? documentId,
            IssuingBody = xDoc.Root?.Element("departamento")?.Value,
            FullText = xDoc.ToString(),
            PublicationDate = DateTime.UtcNow, // Stub
            IsActive = true,
            Metadata = "{}"
        };
    }

    protected override async Task<IEnumerable<RawFragment>> ExtractFragmentsAsync(string blobPath, string documentId, ParentDocument parentDoc, CancellationToken cancellationToken)
    {
        var xmlBlobPath = $"raw-xml/BOE/{documentId}.xml";
        using var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = xmlBlobPath }, cancellationToken);
        var xDoc = await XDocument.LoadAsync(response.ResponseStream, LoadOptions.None, cancellationToken);

        var articulos = xDoc.Descendants("articulo").ToList();
        if (!articulos.Any())
        {
            articulos.Add(new XElement("articulo", new XAttribute("id", "1"), xDoc.Root?.Value ?? ""));
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
}


