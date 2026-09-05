using AsistenteAyuntamiento.Domain.Common.Enums;
using System.Diagnostics;
using System.Xml.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.AI;

namespace AsistenteAyuntamiento.Worker.Services;

public class BoeIngestionService(
    IAmazonS3 s3Client,
    AppDbContext dbContext,
    IFragmentEnrichmentService enrichmentService,
    IIngestionMetricsService metricsService,
    ILogger<BoeIngestionService> logger,
    Kernel kernel) : IHierarchicalIngestionProcessor
{
    private readonly IAmazonS3 _s3Client = s3Client;
    private readonly string _bucketName = AsistenteAyuntamiento.Shared.AppConstants.BlobStorage.DefaultBucketName;
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IFragmentEnrichmentService _enrichmentService = enrichmentService;
    private readonly IIngestionMetricsService _metricsService = metricsService;
    private readonly ILogger<BoeIngestionService> _logger = logger;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    public async Task ProcessDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        int totalLlmCalls = 0;
        int totalLlmTokens = 0;
        int totalTokensEmbedded = 0;
        int chunksGenerated = 0;

        try
        {
            // 1. Download XML from S3 (the queue message sends the json path, we need the raw xml path)
            var xmlBlobPath = $"raw-xml/BOE/{documentId}.xml";
            using var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = xmlBlobPath }, cancellationToken);
            var xDoc = await XDocument.LoadAsync(response.ResponseStream, LoadOptions.None, cancellationToken);

            // 2. Parse Parent Document (Stub logic - would map actual BOE XML fields)
            var parentDoc = new ParentDocument
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

            _dbContext.ParentDocuments.Add(parentDoc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // 3. Extract and process child fragments (e.g. <articulo>)
            var articulos = xDoc.Descendants("articulo").ToList();

            // If no articles found, fallback to whole document as one chunk for this stub
            if (!articulos.Any())
            {
                articulos.Add(new XElement("articulo", new XAttribute("id", "1"), xDoc.Root?.Value ?? ""));
            }

            foreach (var articulo in articulos)
            {
                var normSection = articulo.Attribute("id")?.Value ?? "Artículo Único";
                var originalText = articulo.Value;

                if (string.IsNullOrWhiteSpace(originalText)) continue;

                var lines = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextLines(originalText, 200);
                var paragraphs = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextParagraphs(lines, 400, 50);

                for (int i = 0; i < paragraphs.Count; i++)
                {
                    if (i % 10 == 0 || i == paragraphs.Count - 1)
                    {
                        _logger.LogInformation($"[BOE {documentId}] Procesando fragmento {i + 1}/{paragraphs.Count} de la sección '{normSection}'...");
                    }

                    var paragraph = paragraphs[i];
                    var currentSection = paragraphs.Count > 1 ? $"{normSection} (parte {i + 1})" : normSection;

                    // 4. Enrich fragment
                    var enrichmentResult = await _enrichmentService.EnrichFragmentAsync(
                        BulletinType.BOE,
                        parentDoc.IssuingBody ?? "Estado",
                        parentDoc.NormTitle,
                        currentSection,
                        "General",
                        paragraph,
                        cancellationToken);

                    totalLlmCalls += enrichmentResult.LlmCalls;
                    totalLlmTokens += enrichmentResult.LlmTokens;

                    if (string.IsNullOrWhiteSpace(enrichmentResult.EnrichedText)) continue;

                    // 5. Embed fragment
                    var embeddings = await _embeddingService.GenerateAsync(new List<string> { enrichmentResult.EnrichedText }, cancellationToken: cancellationToken);
                    var embeddingVector = new Pgvector.Vector(embeddings[0].Vector.ToArray());
                    totalTokensEmbedded += enrichmentResult.EnrichedText.Length / 4; // Estimate

                    var childFragment = new ChildFragment
                    {
                        ParentId = parentDoc.Id,
                        Bulletin = BulletinType.BOE,
                        SubSection = currentSection,
                        ChunkText = enrichmentResult.EnrichedText,
                        Embedding = embeddingVector
                    };

                    _dbContext.ChildFragments.Add(childFragment);
                    chunksGenerated++;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            // 6. Record Metrics
            sw.Stop();
            await _metricsService.TrackIngestionAsync(PipelineType.Hierarchical, BulletinType.BOE, documentId, totalTokensEmbedded, totalLlmCalls, totalLlmTokens, chunksGenerated, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando BOE {DocumentId}", documentId);
            throw;
        }
    }
}
