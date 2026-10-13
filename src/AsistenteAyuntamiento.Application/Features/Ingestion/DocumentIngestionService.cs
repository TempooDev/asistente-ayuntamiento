using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using System.Text.Json;
using Qdrant.Client;
using AsistenteAyuntamiento.Domain.Common.Enums;
using static Qdrant.Client.Grpc.Conditions;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class DocumentIngestionService(IAmazonS3 s3Client, IConfiguration config, IAppDbContext dbContext, Kernel kernel, QdrantClient qdrantClient, ILogger<DocumentIngestionService> logger, IChunkingStrategy chunkingStrategy, INotificationService notificationService, IVectorCollections vectorCollections) : IDocumentIngestionService
{
    private readonly string _bucketName = config["Blob:BucketName"] ?? AsistenteAyuntamiento.Shared.AppConstants.BlobStorage.DefaultBucketName;

    public async Task ProcessBlobAsync(string blobPath, string source, CancellationToken cancellationToken = default)
    {
        var docIdFromPath = blobPath.Split('/').LastOrDefault()?.Replace(".json", "") ?? blobPath;

        if (source.Equals("BOPMA", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("BOPMA document ingestion is discarded. Skipping {DocumentId}.", docIdFromPath);
            return;
        }

        await SetJobStateAsync(docIdFromPath, JobStatus.Processing, null, cancellationToken);

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

        logger.LogInformation("JSON descargado ({Length} caracteres). Deserializando", jsonContent.Length);

        var document = JsonSerializer.Deserialize<ScrapedDocument>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (document == null)
        {
            logger.LogWarning("El blob {BlobPath} no se pudo deserializar", blobPath);
            return;
        }

        logger.LogInformation("DocumentId deserializado: {DocumentId}, longitud del texto: {Length}", document.DocumentId, document.Content?.Length ?? 0);

        // 2. Chunking
        List<string> paragraphs;
        if (string.IsNullOrWhiteSpace(document.Content))
        {
            logger.LogInformation("El blob {BlobPath} tiene texto vacío; se guardará un chunk con sus metadatos", blobPath);
            var title = !string.IsNullOrWhiteSpace(document.Metadata?.Title) ? document.Metadata.Title : "Documento sin contenido";
            paragraphs = new List<string> { title };
        }
        else
        {
            paragraphs = chunkingStrategy.ChunkText(document.Content).Select(c => c.Text).ToList();
        }

        // 3. Obtener servicio de embeddings
        var embeddingGenerator = kernel.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();

        // 4. Vectorización
        int batchSize = 100;
        var allEmbeddings = new List<Microsoft.Extensions.AI.Embedding<float>>();
        var chunkedParagraphs = paragraphs.Chunk(batchSize).ToList();
        for (int i = 0; i < chunkedParagraphs.Count; i++)
        {
            logger.LogInformation("[Baseline {DocumentId}] Vectorizando lote {Batch}/{Batches}", document.DocumentId, i + 1, chunkedParagraphs.Count);
            var batchEmbeddings = await embeddingGenerator.GenerateAsync(chunkedParagraphs[i].ToList(), cancellationToken: cancellationToken);
            allEmbeddings.AddRange(batchEmbeddings);
        }

        // 5. PostgreSQL first: it is the source of truth. Vectors are written only after the commit.
        var publicationDate = PublicationDateParser.TryParse(document.Metadata?.PublicationDate, out var date) ? date : DateTime.UtcNow;
        var chunks = paragraphs.Select((p, i) => new DocumentChunk
        {
            DocumentId = document.DocumentId,
            Source = source,
            Title = document.Metadata?.Title ?? string.Empty,
            Department = document.Metadata?.Department ?? string.Empty,
            Content = p,
            ChunkIndex = i,
            PublicationDate = publicationDate
        }).ToList();

        try
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                // The execution strategy may re-run this block: start each attempt from a clean state.
                dbContext.ChangeTracker.Clear();
                foreach (var chunk in chunks) chunk.Id = 0;

                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

                await dbContext.DocumentChunks
                    .Where(c => c.DocumentId == document.DocumentId)
                    .ExecuteDeleteAsync(cancellationToken);

                await dbContext.DocumentChunks.AddRangeAsync(chunks, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (Exception ex)
        {
            await SetJobStateAsync(document.DocumentId, JobStatus.Failed, ex.Message, cancellationToken);
            throw;
        }

        // 6. Vector store: replace every vector of this document (old chunk ids would otherwise be orphaned).
        try
        {
            var vectors = allEmbeddings.Select(e => e.Vector.ToArray()).ToList();
            await vectorCollections.EnsureAsync(VectorCollectionNames.DocumentChunks, vectors[0].Length, cancellationToken);
            await qdrantClient.DeleteAsync(VectorCollectionNames.DocumentChunks, MatchKeyword("DocumentId", document.DocumentId), cancellationToken: cancellationToken);

            var points = chunks.Select((chunk, i) => new Qdrant.Client.Grpc.PointStruct
            {
                Id = new Qdrant.Client.Grpc.PointId { Num = (ulong)chunk.Id },
                Vectors = vectors[i],
                Payload =
                {
                    ["DocumentId"] = chunk.DocumentId,
                    ["Source"] = chunk.Source,
                    ["Title"] = chunk.Title ?? "",
                    ["Department"] = chunk.Department ?? "",
                    ["Content"] = chunk.Content,
                    ["ChunkIndex"] = chunk.ChunkIndex,
                    ["PublicationDate"] = chunk.PublicationDate.ToString("O")
                }
            }).ToList();

            await qdrantClient.UpsertAsync(VectorCollectionNames.DocumentChunks, points, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // A retry of the message re-runs the whole document idempotently and re-syncs the vectors.
            await SetJobStateAsync(document.DocumentId, JobStatus.VectorSyncFailed, ex.Message, cancellationToken);
            throw;
        }

        await SetJobStateAsync(document.DocumentId, JobStatus.Completed, null, cancellationToken);
        logger.LogInformation("Documento {DocumentId} vectorizado con {ChunkCount} chunks", document.DocumentId, chunks.Count);
    }

    /// <summary>Records the job state and notifies; failures here are logged so they never mask the original error.</summary>
    private async Task SetJobStateAsync(string documentId, JobStatus status, string? error, CancellationToken cancellationToken)
    {
        try
        {
            var jobState = await dbContext.DocumentJobStates.FirstOrDefaultAsync(j => j.DocumentId == documentId, cancellationToken);
            if (jobState is null)
            {
                dbContext.DocumentJobStates.Add(new DocumentJobState { DocumentId = documentId, Status = status, ErrorMessage = error });
            }
            else
            {
                jobState.Status = status;
                jobState.ErrorMessage = error;
                jobState.LastUpdatedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await notificationService.NotifyDocumentStatusChangedAsync(documentId, status.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not set document {DocumentId} to {Status}", documentId, status);
        }
    }
}
