using AsistenteAyuntamiento.Domain.Common.Enums;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Qdrant.Client;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class BojaIngestionService : BaseHierarchicalIngestionProcessor
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName = AsistenteAyuntamiento.Shared.AppConstants.BlobStorage.DefaultBucketName;

    public BojaIngestionService(
        IAmazonS3 s3Client,
        IAppDbContext dbContext,
        IFragmentEnrichmentService enrichmentService,
        IIngestionMetricsService metricsService,
        QdrantClient qdrantClient,
        ILogger<BojaIngestionService> logger,
        Kernel kernel) 
        : base(dbContext, enrichmentService, metricsService, qdrantClient, logger, kernel)
    {
        _s3Client = s3Client;
    }

    protected override BulletinType Bulletin => BulletinType.BOJA;

    protected override async Task<ParentDocument?> ExtractParentDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken)
    {
        using var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = blobPath }, cancellationToken);
        using var reader = new StreamReader(response.ResponseStream);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        using var doc = JsonDocument.Parse(jsonString);
        var root = doc.RootElement;

        return new ParentDocument
        {
            Bulletin = BulletinType.BOJA,
            DocumentId = documentId,
            NormTitle = root.TryGetProperty("titulo", out var t) ? t.GetString() ?? documentId : documentId,
            IssuingBody = root.TryGetProperty("organismo", out var o) ? o.GetString() : "Junta de Andalucía",
            FullText = jsonString,
            PublicationDate = DateTime.UtcNow, // Stub
            IsActive = true,
            Metadata = "{}"
        };
    }

    protected override async Task<IEnumerable<RawFragment>> ExtractFragmentsAsync(string blobPath, string documentId, ParentDocument parentDoc, CancellationToken cancellationToken)
    {
        using var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = blobPath }, cancellationToken);
        using var reader = new StreamReader(response.ResponseStream);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        using var doc = JsonDocument.Parse(jsonString);
        var root = doc.RootElement;

        var rawText = root.TryGetProperty("texto", out var txt) ? txt.GetString() ?? "" : jsonString;
        int chunkSize = 1000;
        
        var result = new List<RawFragment>();

        for (int i = 0; i < rawText.Length; i += chunkSize)
        {
            var originalText = rawText.Substring(i, Math.Min(chunkSize, rawText.Length - i));
            var normSection = $"Sección {i / chunkSize + 1}";

            result.Add(new RawFragment
            {
                Section = normSection,
                ElementType = "Párrafo",
                OriginalText = originalText
            });
        }

        return result;
    }
}


