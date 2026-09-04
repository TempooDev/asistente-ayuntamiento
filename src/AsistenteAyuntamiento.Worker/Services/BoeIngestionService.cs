using System.Diagnostics;
using System.Xml.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using AsistenteAyuntamiento.Application.Common;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Text;
using Microsoft.SemanticKernel.Embeddings;

namespace AsistenteAyuntamiento.Worker.Services;

public interface IHierarchicalIngestionProcessor
{
    Task ProcessDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken);
}

public class BoeIngestionService : IHierarchicalIngestionProcessor
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName = "boletines";
    private readonly AppDbContext _dbContext;
    private readonly FragmentEnrichmentService _enrichmentService;
    private readonly IngestionMetricsService _metricsService;
    private readonly ILogger<BoeIngestionService> _logger;

#pragma warning disable SKEXP0001
    private readonly ITextEmbeddingGenerationService _embeddingService;
#pragma warning restore SKEXP0001

    public BoeIngestionService(
        IAmazonS3 s3Client, 
        AppDbContext dbContext, 
        FragmentEnrichmentService enrichmentService, 
        IngestionMetricsService metricsService, 
        ILogger<BoeIngestionService> logger, 
        Kernel kernel)
    {
        _s3Client = s3Client;
        _dbContext = dbContext;
        _enrichmentService = enrichmentService;
        _metricsService = metricsService;
        _logger = logger;
#pragma warning disable SKEXP0001
        _embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
#pragma warning restore SKEXP0001
    }

    public async Task ProcessDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        int totalLlmCalls = 0;
        int totalLlmTokens = 0;
        int totalTokensEmbedded = 0;
        int chunksGenerated = 0;

        try
        {
            // 1. Download XML from S3
            using var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = blobPath }, cancellationToken);
            var xDoc = await XDocument.LoadAsync(response.ResponseStream, LoadOptions.None, cancellationToken);

            // 2. Parse Parent Document (Stub logic - would map actual BOE XML fields)
            var parentDoc = new ParentDocument
            {
                Bulletin = DocumentSources.BOE,
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

                // 4. Enrich fragment
                var enrichmentResult = await _enrichmentService.EnrichFragmentAsync(
                    DocumentSources.BOE, 
                    parentDoc.IssuingBody ?? "Estado", 
                    parentDoc.NormTitle, 
                    normSection, 
                    "General", 
                    originalText, 
                    cancellationToken);

                totalLlmCalls += enrichmentResult.LlmCalls;
                totalLlmTokens += enrichmentResult.LlmTokens;

                // 5. Embed fragment
#pragma warning disable SKEXP0001
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(new List<string> { enrichmentResult.EnrichedText }, cancellationToken: cancellationToken);
                var embeddingVector = new Pgvector.Vector(embeddings.First().ToArray());
                totalTokensEmbedded += enrichmentResult.EnrichedText.Length / 4; // Estimate
#pragma warning restore SKEXP0001

                var childFragment = new ChildFragment
                {
                    ParentId = parentDoc.Id,
                    Bulletin = DocumentSources.BOE,
                    SubSection = normSection,
                    ChunkText = enrichmentResult.EnrichedText,
                    Embedding = embeddingVector
                };

                _dbContext.ChildFragments.Add(childFragment);
                chunksGenerated++;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            // 6. Record Metrics
            sw.Stop();
            await _metricsService.TrackIngestionAsync(PipelineModes.HIERARCHICAL, DocumentSources.BOE, documentId, totalTokensEmbedded, totalLlmCalls, totalLlmTokens, chunksGenerated, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando BOE {DocumentId}", documentId);
            throw;
        }
    }
}
