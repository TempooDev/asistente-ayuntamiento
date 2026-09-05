using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Text;
using System.Text.Json;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class DocumentIngestionService(IAmazonS3 s3Client, IConfiguration config, IAppDbContext dbContext, Kernel kernel, ILogger<DocumentIngestionService> logger, INotificationService? notificationService = null) : IDocumentIngestionService
{
        private readonly string _bucketName = config["Blob:BucketName"] ?? AsistenteAyuntamiento.Shared.AppConstants.BlobStorage.DefaultBucketName;
                    
    public async Task ProcessBlobAsync(string blobPath, string source, CancellationToken cancellationToken = default)
    {
        var docIdFromPath = blobPath.Split('/').LastOrDefault()?.Replace(".json", "") ?? blobPath;
        var initialJobState = await dbContext.DocumentJobStates.FirstOrDefaultAsync(j => j.DocumentId == docIdFromPath, cancellationToken);

        if (initialJobState != null)
        {
            initialJobState.Status = "Processing";
            initialJobState.LastUpdatedAt = DateTime.UtcNow;
            initialJobState.ErrorMessage = null;
        }
        else
        {
            dbContext.DocumentJobStates.Add(new DocumentJobState
            {
                DocumentId = docIdFromPath,
                Status = "Processing"
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await notificationService?.NotifyDocumentStatusChangedAsync(docIdFromPath, "Processing");

        // 1. Descargar JSON desde S3/MinIO
        string jsonContent;
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = blobPath
            };
            using var response = await s3Client.GetObjectAsync(request, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            jsonContent = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            throw new Exception($"El blob {blobPath} no existe en S3/MinIO.", ex);
        }

        logger.LogInformation($"JSON descargado ({jsonContent.Length} caracteres). Deserializando...");

        var document = JsonSerializer.Deserialize<ScrapedDocument>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (document == null)
        {
            logger.LogWarning($"El blob {blobPath} no se pudo deserializar (document es null).");
            return;
        }

        logger.LogInformation($"DocumentId deserializado: '{document.DocumentId}', Longitud del texto: {document.Content?.Length ?? 0}");

        // 2. Chunking
        List<string> paragraphs;
        if (string.IsNullOrWhiteSpace(document.Content))
        {
            logger.LogInformation($"El blob {blobPath} tiene texto vacío. Se guardará un chunk con sus metadatos.");
            // Usar el título como contenido para que el motor vectorial pueda encontrarlo semánticamente
            var title = !string.IsNullOrWhiteSpace(document.Metadata?.Title) ? document.Metadata.Title : "Documento sin contenido";
            paragraphs = new List<string> { title };
        }
        else
        {
            var maxLines = config.GetValue<int>("Ai:Embeddings:ChunkMaxLines", 200);
            var maxTokens = config.GetValue<int>("Ai:Embeddings:ChunkMaxTokens", 400);
            var overlapTokens = config.GetValue<int>("Ai:Embeddings:ChunkOverlapTokens", 50);

            paragraphs = TextChunker.SplitPlainTextParagraphs(
                TextChunker.SplitPlainTextLines(document.Content, maxLines),
                maxTokens,
                overlapTokens
            );
        }

        // 3. Obtener servicio de embeddings
        var embeddingGenerator = kernel.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();

        // 4. Vectorización (en batch segmentado para evitar límites de payload)
        int batchSize = 100;
        var allEmbeddings = new List<Microsoft.Extensions.AI.Embedding<float>>();
        var chunkedParagraphs = paragraphs.Chunk(batchSize).ToList();
        for (int i = 0; i < chunkedParagraphs.Count; i++)
        {
            logger.LogInformation($"[Baseline {document.DocumentId}] Vectorizando lote {i + 1}/{chunkedParagraphs.Count}...");
            var batchEmbeddings = await embeddingGenerator.GenerateAsync(chunkedParagraphs[i].ToList(), cancellationToken: cancellationToken);
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
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Eliminar los chunks anteriores directamente en base de datos de manera atómica
                await dbContext.DocumentChunks
                    .Where(c => c.DocumentId == document.DocumentId)
                    .ExecuteDeleteAsync(cancellationToken);

                await dbContext.DocumentChunks.AddRangeAsync(chunks, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                // Update state to Completed using ExecuteUpdateAsync to ensure it bypasses change tracker issues
                var updatedRows = await dbContext.DocumentJobStates
                    .Where(j => j.DocumentId == document.DocumentId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, "Completed")
                        .SetProperty(p => p.LastUpdatedAt, DateTime.UtcNow)
                        .SetProperty(p => p.ErrorMessage, (string?)null),
                        cancellationToken);

                if (updatedRows == 0)
                {
                    dbContext.DocumentJobStates.Add(new DocumentJobState
                    {
                        DocumentId = document.DocumentId,
                        Status = "Completed",
                        LastUpdatedAt = DateTime.UtcNow
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                await notificationService?.NotifyDocumentStatusChangedAsync(document.DocumentId, "Completed");

                logger.LogInformation($"Documento {document.DocumentId} vectorizado exitosamente con {chunks.Count} chunks.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);

                // Tratar de registrar el fallo si tenemos un DocumentId
                try
                {
                    var fallbackDocId = document?.DocumentId ?? blobPath.Split('/').LastOrDefault()?.Replace(".json", "") ?? blobPath;
                    var updatedRows = await dbContext.DocumentJobStates
                        .Where(j => j.DocumentId == fallbackDocId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.Status, "Failed")
                            .SetProperty(p => p.LastUpdatedAt, DateTime.UtcNow)
                            .SetProperty(p => p.ErrorMessage, ex.Message),
                            cancellationToken);

                    if (updatedRows == 0)
                    {
                        dbContext.DocumentJobStates.Add(new DocumentJobState
                        {
                            DocumentId = fallbackDocId,
                            Status = "Failed",
                            LastUpdatedAt = DateTime.UtcNow,
                            ErrorMessage = ex.Message
                        });
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }

                    await notificationService?.NotifyDocumentStatusChangedAsync(fallbackDocId, "Failed");
                }
                catch { /* Ignore inner failure */ }

                throw;
            }
        });
    }
}


