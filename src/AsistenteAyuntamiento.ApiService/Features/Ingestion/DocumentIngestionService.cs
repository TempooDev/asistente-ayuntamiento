using AsistenteAyuntamiento.ApiService.Features.Ingestion.Models;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Text;
using System.Text.Json;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
#pragma warning disable SKEXP0001

namespace AsistenteAyuntamiento.ApiService.Features.Ingestion;

public class DocumentIngestionService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly AppDbContext _dbContext;
    private readonly Kernel _kernel;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(IAmazonS3 s3Client, IConfiguration config, AppDbContext dbContext, Kernel kernel, ILogger<DocumentIngestionService> logger)
    {
        _s3Client = s3Client;
        _bucketName = config["Blob:BucketName"] ?? "boletines";
        _dbContext = dbContext;
        _kernel = kernel;
        _logger = logger;
    }

    public async Task ProcessBlobAsync(string blobPath, string source, CancellationToken cancellationToken = default)
    {
        var docIdFromPath = blobPath.Split('/').LastOrDefault()?.Replace(".json", "") ?? blobPath;
        var initialJobState = await _dbContext.DocumentJobStates.FirstOrDefaultAsync(j => j.DocumentId == docIdFromPath, cancellationToken);
        
        if (initialJobState != null && initialJobState.Status == "Processing")
        {
            _logger.LogWarning($"El documento {docIdFromPath} ya está en estado 'Processing'. Previniendo duplicidad.");
            return;
        }

        if (initialJobState != null)
        {
            initialJobState.Status = "Processing";
            initialJobState.LastUpdatedAt = DateTime.UtcNow;
            initialJobState.ErrorMessage = null;
        }
        else
        {
            _dbContext.DocumentJobStates.Add(new DocumentJobState 
            { 
                DocumentId = docIdFromPath, 
                Status = "Processing" 
            });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 1. Descargar JSON desde S3/MinIO
        string jsonContent;
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = blobPath
            };
            using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            jsonContent = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            throw new Exception($"El blob {blobPath} no existe en S3/MinIO.", ex);
        }

        _logger.LogInformation($"JSON descargado ({jsonContent.Length} caracteres). Deserializando...");
        
        var document = JsonSerializer.Deserialize<ScrapedDocument>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (document == null)
        {
            _logger.LogWarning($"El blob {blobPath} no se pudo deserializar (document es null).");
            return;
        }
        
        _logger.LogInformation($"DocumentId deserializado: '{document.DocumentId}', Longitud del texto: {document.Content?.Length ?? 0}");

        // 2. Chunking
        List<string> paragraphs;
        if (string.IsNullOrWhiteSpace(document.Content))
        {
            _logger.LogInformation($"El blob {blobPath} tiene texto vacío. Se guardará un chunk con sus metadatos.");
            // Usar el título como contenido para que el motor vectorial pueda encontrarlo semánticamente
            var title = !string.IsNullOrWhiteSpace(document.Metadata?.Title) ? document.Metadata.Title : "Documento sin contenido";
            paragraphs = new List<string> { title };
        }
        else
        {
#pragma warning disable SKEXP0050
            paragraphs = TextChunker.SplitPlainTextParagraphs(
                TextChunker.SplitPlainTextLines(document.Content, 200),
                400,
                50 // overlap
            );
#pragma warning restore SKEXP0050
        }

        // 3. Obtener servicio de embeddings
        var embeddingGenerator = _kernel.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();

        // 4. Vectorización (en batch segmentado para evitar límites de payload)
        int batchSize = 100;
        var allEmbeddings = new List<Microsoft.Extensions.AI.Embedding<float>>();
        foreach (var batch in paragraphs.Chunk(batchSize))
        {
            var batchEmbeddings = await embeddingGenerator.GenerateAsync(batch.ToList(), cancellationToken: cancellationToken);
            allEmbeddings.AddRange(batchEmbeddings);
        }

        var chunks = new List<DocumentChunk>();
        for (int i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i];
            var embedding = allEmbeddings[i].Vector;
            
            var chunk = new DocumentChunk
            {
                DocumentId = document.DocumentId,
                Source = source,
                Title = document.Metadata?.Title ?? string.Empty,
                Department = document.Metadata?.Department ?? string.Empty,
                Content = p,
                ChunkIndex = i,
                PublicationDate = DateTime.TryParse(document.Metadata?.PublicationDate, out var date) ? date.ToUniversalTime() : DateTime.UtcNow,
                Embedding = new Pgvector.Vector(embedding.ToArray())
            };
            chunks.Add(chunk);
        }

        // 5. Persistencia transaccional
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Eliminar los chunks anteriores directamente en base de datos de manera atómica
                await _dbContext.DocumentChunks
                    .Where(c => c.DocumentId == document.DocumentId)
                    .ExecuteDeleteAsync(cancellationToken);

                await _dbContext.DocumentChunks.AddRangeAsync(chunks, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                
                // Update state to Completed
                var jobState = await _dbContext.DocumentJobStates.FirstOrDefaultAsync(j => j.DocumentId == document.DocumentId, cancellationToken);
                if (jobState != null)
                {
                    jobState.Status = "Completed";
                    jobState.LastUpdatedAt = DateTime.UtcNow;
                    jobState.ErrorMessage = null;
                }
                else
                {
                    _dbContext.DocumentJobStates.Add(new DocumentJobState 
                    { 
                        DocumentId = document.DocumentId, 
                        Status = "Completed" 
                    });
                }
                await _dbContext.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                
                _logger.LogInformation($"Documento {document.DocumentId} vectorizado exitosamente con {chunks.Count} chunks.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                
                // Tratar de registrar el fallo si tenemos un DocumentId
                try 
                {
                    var fallbackDocId = document?.DocumentId ?? blobPath.Split('/').LastOrDefault()?.Replace(".json", "") ?? blobPath;
                    var jobState = await _dbContext.DocumentJobStates.FirstOrDefaultAsync(j => j.DocumentId == fallbackDocId, cancellationToken);
                    if (jobState != null)
                    {
                        jobState.Status = "Failed";
                        jobState.LastUpdatedAt = DateTime.UtcNow;
                        jobState.ErrorMessage = ex.Message;
                    }
                    else
                    {
                        _dbContext.DocumentJobStates.Add(new DocumentJobState 
                        { 
                            DocumentId = fallbackDocId, 
                            Status = "Failed",
                            ErrorMessage = ex.Message
                        });
                    }
                    await _dbContext.SaveChangesAsync(cancellationToken);
                } 
                catch { /* Ignore inner failure */ }
                
                throw;
            }
        });
    }
}
#pragma warning restore SKEXP0001
