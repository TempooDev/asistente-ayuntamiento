using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// Ingestion of one flat-chunk variant (<see cref="FlatChunkVariant"/>): download, chunk with the variant's strategy,
/// embed, replace the variant's <c>DocumentChunks</c> rows and points, record its job status and metrics. The rows and
/// points of the other variants are never touched.
/// </summary>
public class DocumentIngestionService(
    IBlobStorage blobStorage,
    IAppDbContext dbContext,
    Kernel kernel,
    IVectorStore vectorStore,
    ILogger<DocumentIngestionService> logger,
    IChunkingStrategy chunkingStrategy,
    INotificationService notificationService,
    IIngestionMetricsService metricsService,
    FlatChunkVariant variant) : IDocumentIngestionService
{
    public async Task ProcessBlobAsync(string blobPath, string source, CancellationToken cancellationToken = default)
    {
        // The id from the path (json/{SOURCE}/{id}.json) is the key of the job state, the chunks and the vectors, as in
        // the queue message and the hierarchical pipeline, so every state of this run lands on the same row.
        var documentId = blobPath.Split('/').LastOrDefault()?.Replace(".json", "") ?? blobPath;

        // Only the bulletins in scope (BOE, BOJA) are ingested.
        if (!Enum.GetNames<BulletinType>().Contains(source, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogWarning("Unsupported source {Source}; document {DocumentId} is discarded", source, documentId);
            return;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await SetJobStateAsync(documentId, JobStatus.Processing, null, cancellationToken);

        // 1. Descargar JSON desde S3/MinIO
        string jsonContent;
        await using (var stream = await blobStorage.OpenReadAsync(blobPath, cancellationToken))
        using (var reader = new StreamReader(stream))
        {
            jsonContent = await reader.ReadToEndAsync(cancellationToken);
        }

        logger.LogInformation("JSON descargado ({Length} caracteres). Deserializando", jsonContent.Length);

        ScrapedDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ScrapedDocument>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "El blob {BlobPath} no es un JSON válido", blobPath);
            document = null;
        }

        if (document == null)
        {
            // Retrying cannot fix the blob: the run ends Failed instead of leaving the document in Processing.
            logger.LogWarning("El blob {BlobPath} no se pudo deserializar", blobPath);
            await SetJobStateAsync(documentId, JobStatus.Failed, $"El blob {blobPath} no contiene un documento válido.", cancellationToken);
            return;
        }

        if (!string.IsNullOrEmpty(document.DocumentId) && !string.Equals(document.DocumentId, documentId, StringComparison.Ordinal))
        {
            logger.LogWarning("El blob {BlobPath} declara el id {DeclaredId}; se usa el id de la ruta {DocumentId}", blobPath, document.DocumentId, documentId);
        }

        logger.LogInformation("DocumentId deserializado: {DocumentId}, longitud del texto: {Length}", documentId, document.Content?.Length ?? 0);

        // 2. Chunking
        List<string> paragraphs;
        var chunkingTokens = 0;
        if (string.IsNullOrWhiteSpace(document.Content))
        {
            logger.LogInformation("El blob {BlobPath} tiene texto vacío; se guardará un chunk con sus metadatos", blobPath);
            var title = !string.IsNullOrWhiteSpace(document.Metadata?.Title) ? document.Metadata.Title : "Documento sin contenido";
            paragraphs = new List<string> { title };
        }
        else
        {
            var chunkResults = await chunkingStrategy.ChunkTextAsync(document.Content, cancellationToken);
            paragraphs = chunkResults.Select(c => c.Text).ToList();
            chunkingTokens = chunkResults.Sum(c => c.EmbeddedTokens ?? 0);
        }

        // 3. Obtener servicio de embeddings
        var embeddingGenerator = kernel.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();

        // 4. Vectorización
        int batchSize = 100;
        var allEmbeddings = new List<Microsoft.Extensions.AI.Embedding<float>>();
        var chunkedParagraphs = paragraphs.Chunk(batchSize).ToList();
        for (int i = 0; i < chunkedParagraphs.Count; i++)
        {
            logger.LogInformation("[{Pipeline} {DocumentId}] Vectorizando lote {Batch}/{Batches}", variant.Pipeline, documentId, i + 1, chunkedParagraphs.Count);
            var batchEmbeddings = await embeddingGenerator.GenerateAsync(chunkedParagraphs[i].ToList(), cancellationToken: cancellationToken);
            allEmbeddings.AddRange(batchEmbeddings);
        }

        // 5. PostgreSQL first: it is the source of truth. Vectors are written only after the commit.
        var publicationDate = PublicationDateParser.TryParse(document.Metadata?.PublicationDate, out var date) ? date : DateTime.UtcNow;
        var municipalityKey = MunicipalityExtractor.ToMatchKey(MunicipalityExtractor.Extract(document.Metadata?.Title, document.Metadata?.Department));
        var chunks = paragraphs.Select((p, i) => new DocumentChunk
        {
            DocumentId = documentId,
            Source = source,
            Title = document.Metadata?.Title ?? string.Empty,
            Department = document.Metadata?.Department ?? string.Empty,
            Content = p,
            ChunkIndex = i,
            PublicationDate = publicationDate,
            ChunkingStrategy = variant.ChunkingStrategy
        }).ToList();

        try
        {
            await dbContext.ExecuteInTransactionAsync(async ct =>
            {
                // The execution strategy may re-run this block: start each attempt from a clean state.
                dbContext.ChangeTracker.Clear();
                foreach (var chunk in chunks) chunk.Id = 0;

                await dbContext.DocumentChunks
                    .Where(c => c.DocumentId == documentId && c.ChunkingStrategy == variant.ChunkingStrategy)
                    .ExecuteDeleteAsync(ct);

                await dbContext.DocumentChunks.AddRangeAsync(chunks, ct);
                await dbContext.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await SetJobStateAsync(documentId, JobStatus.Failed, ex.Message, cancellationToken);
            throw;
        }

        // 6. Vector store: replace every vector of this document (old chunk ids would otherwise be orphaned).
        try
        {
            var vectors = allEmbeddings.Select(e => e.Vector.ToArray()).ToList();
            await vectorStore.EnsureCollectionAsync(variant.Collection, vectors[0].Length, cancellationToken);
            await vectorStore.DeleteDocumentAsync(variant.Collection, documentId, cancellationToken: cancellationToken);

            var points = chunks.Select((chunk, i) => new VectorPoint(chunk.Id, vectors[i], new Dictionary<string, object>
            {
                [VectorPayloadKeys.DocumentId] = chunk.DocumentId,
                [VectorPayloadKeys.Source] = chunk.Source,
                [VectorPayloadKeys.Title] = chunk.Title ?? "",
                [VectorPayloadKeys.Department] = chunk.Department ?? "",
                [VectorPayloadKeys.Municipality] = municipalityKey ?? "", // same match key as the hierarchical payload
                [VectorPayloadKeys.Content] = chunk.Content,
                [VectorPayloadKeys.ChunkIndex] = chunk.ChunkIndex,
                [VectorPayloadKeys.PublicationDate] = chunk.PublicationDate.ToString("O")
            })).ToList();

            await vectorStore.UpsertAsync(variant.Collection, points, cancellationToken);
        }
        catch (Exception ex)
        {
            // A retry of the message re-runs the whole document idempotently and re-syncs the vectors.
            await SetJobStateAsync(documentId, JobStatus.VectorSyncFailed, ex.Message, cancellationToken);
            throw;
        }

        await SetJobStateAsync(documentId, JobStatus.Completed, null, cancellationToken);
        logger.LogInformation("Documento {DocumentId} vectorizado con {ChunkCount} chunks ({Strategy})", documentId, chunks.Count, variant.ChunkingStrategy);

        // Same IngestionMetrics as the hierarchical pipeline, so the variants can be compared (no LLM calls here).
        // Embedded tokens include those the strategy embedded to chunk (semantic sentence windows).
        stopwatch.Stop();
        await metricsService.TrackIngestionAsync(
            variant.Pipeline,
            Enum.Parse<BulletinType>(source, ignoreCase: true),
            documentId,
            tokensEmbedded: paragraphs.Sum(ChunkTokenCounter.Count) + chunkingTokens, // estimate, as in the hierarchical pipeline
            llmCalls: 0,
            llmTokens: 0,
            chunksGenerated: chunks.Count,
            processingDurationMs: stopwatch.ElapsedMilliseconds,
            cancellationToken);
    }

    /// <summary>Records the job state and notifies; failures here are logged so they never mask the original error.</summary>
    private async Task SetJobStateAsync(string documentId, JobStatus status, string? error, CancellationToken cancellationToken)
    {
        try
        {
            // Only this variant's column changes; the notification carries the document's summary.
            var state = await DocumentJobStateUpdates.SetPipelineStatusAsync(dbContext, documentId, variant.Pipeline, status, error, cancellationToken);
            await notificationService.NotifyDocumentStatusChangedAsync(documentId, state.Status.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not set document {DocumentId} to {Status}", documentId, status);
        }
    }
}
