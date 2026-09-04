using System.Diagnostics;
using System.Text.Json;
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

public class BojaIngestionService : IHierarchicalIngestionProcessor
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName = "boletines";
    private readonly AppDbContext _dbContext;
    private readonly FragmentEnrichmentService _enrichmentService;
    private readonly IngestionMetricsService _metricsService;
    private readonly ILogger<BojaIngestionService> _logger;

#pragma warning disable SKEXP0001
    private readonly ITextEmbeddingGenerationService _embeddingService;
#pragma warning restore SKEXP0001

    public BojaIngestionService(
        IAmazonS3 s3Client, 
        AppDbContext dbContext, 
        FragmentEnrichmentService enrichmentService, 
        IngestionMetricsService metricsService, 
        ILogger<BojaIngestionService> logger, 
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
            using var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = blobPath }, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            var jsonString = await reader.ReadToEndAsync(cancellationToken);
            
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            // 2. Parse Parent Document (Stub logic - would map actual BOJA JSON fields)
            var parentDoc = new ParentDocument
            {
                Bulletin = DocumentSources.BOJA,
                DocumentId = documentId,
                NormTitle = root.TryGetProperty("titulo", out var t) ? t.GetString() ?? documentId : documentId,
                IssuingBody = root.TryGetProperty("organismo", out var o) ? o.GetString() : "Junta de Andalucía",
                FullText = jsonString,
                PublicationDate = DateTime.UtcNow, // Stub
                IsActive = true,
                Metadata = "{}"
            };

            _dbContext.ParentDocuments.Add(parentDoc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // 3. Extract and process child fragments (e.g. paragraphs or specific objects)
            // As a stub, we will just chunk the entire text into 500 character blocks
            var rawText = root.TryGetProperty("texto", out var txt) ? txt.GetString() ?? "" : jsonString;
            int chunkSize = 1000;
            for (int i = 0; i < rawText.Length; i += chunkSize)
            {
                var originalText = rawText.Substring(i, Math.Min(chunkSize, rawText.Length - i));
                var normSection = $"Sección {i/chunkSize + 1}";

                var enrichmentResult = await _enrichmentService.EnrichFragmentAsync(
                    DocumentSources.BOJA, 
                    parentDoc.IssuingBody ?? "Junta", 
                    parentDoc.NormTitle, 
                    normSection, 
                    "Párrafo", 
                    originalText, 
                    cancellationToken);

                totalLlmCalls += enrichmentResult.LlmCalls;
                totalLlmTokens += enrichmentResult.LlmTokens;

#pragma warning disable SKEXP0001
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(new List<string> { enrichmentResult.EnrichedText }, cancellationToken: cancellationToken);
                var embeddingVector = new Pgvector.Vector(embeddings.First().ToArray());
                totalTokensEmbedded += enrichmentResult.EnrichedText.Length / 4; // Estimate
#pragma warning restore SKEXP0001

                var childFragment = new ChildFragment
                {
                    ParentId = parentDoc.Id,
                    Bulletin = DocumentSources.BOJA,
                    SubSection = normSection,
                    ChunkText = enrichmentResult.EnrichedText,
                    Embedding = embeddingVector
                };

                _dbContext.ChildFragments.Add(childFragment);
                chunksGenerated++;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            sw.Stop();
            await _metricsService.TrackIngestionAsync(PipelineModes.HIERARCHICAL, DocumentSources.BOJA, documentId, totalTokensEmbedded, totalLlmCalls, totalLlmTokens, chunksGenerated, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando BOJA {DocumentId}", documentId);
            throw;
        }
    }
}
