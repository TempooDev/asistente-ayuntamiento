using Amazon.S3;
using Amazon.S3.Model;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AsistenteAyuntamiento.Application.Common.Messaging;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Application.Common.VectorStore;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class IngestionAdminService : IIngestionAdminService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _config;
    private readonly IAppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IIngestionQueuePublisher _publisher;
    private readonly IVectorStore _vectorStore;
    private readonly INotificationService _notificationService;
    private readonly ILogger<IngestionAdminService> _logger;
    private readonly string _bucketName;
    private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

    public IngestionAdminService(
        IAmazonS3 s3Client,
        IConfiguration config,
        IAppDbContext dbContext,
        IMemoryCache cache,
        IIngestionQueuePublisher publisher,
        IVectorStore vectorStore,
        INotificationService notificationService,
        ILogger<IngestionAdminService> logger)
    {
        _s3Client = s3Client;
        _config = config;
        _dbContext = dbContext;
        _cache = cache;
        _publisher = publisher;
        _vectorStore = vectorStore;
        _notificationService = notificationService;
        _logger = logger;
        _bucketName = _config["Blob:BucketName"] ?? AsistenteAyuntamiento.Shared.AppConstants.BlobStorage.DefaultBucketName;
    }

    public async Task<BlobListResult> ListBlobsAsync(int? page, int? pageSize, string? status, string? search, DateTime? dateFrom, DateTime? dateTo, int? minSizeKb, int? maxSizeKb, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"blobs_list_{_bucketName}";
        if (!_cache.TryGetValue(cacheKey, out List<BlobItemDto>? allBlobs) || allBlobs == null)
        {
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                if (!_cache.TryGetValue(cacheKey, out allBlobs) || allBlobs == null)
                {
                    allBlobs = new List<BlobItemDto>();
                    var jobStates = await _dbContext.DocumentJobStates
                        .AsNoTracking()
                        .Select(j => new { j.DocumentId, j.Status })
                        .ToDictionaryAsync(j => j.DocumentId, j => j.Status.ToString(), cancellationToken);

                    try
                    {
                        var request = new ListObjectsV2Request { BucketName = _bucketName, Prefix = "json/" };
                        ListObjectsV2Response response;
                        do
                        {
                            response = await _s3Client.ListObjectsV2Async(request, cancellationToken);
                            if (response?.S3Objects != null)
                            {
                                foreach (var s3Obj in response.S3Objects)
                                {
                                    if (s3Obj?.Key == null) continue;
                                    var docId = s3Obj.Key.Split('/').LastOrDefault()?.Replace(".json", "") ?? "";
                                    var objStatus = jobStates.TryGetValue(docId, out var jobStatus) ? jobStatus : nameof(JobStatus.Pending);

                                    allBlobs.Add(new BlobItemDto(s3Obj.Key, s3Obj.Size, s3Obj.LastModified, objStatus == nameof(JobStatus.Completed), objStatus));
                                }
                            }
                            request.ContinuationToken = response?.NextContinuationToken;
                        } while (response?.IsTruncated == true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching objects from S3");
                    }

                    _cache.Set(cacheKey, allBlobs, TimeSpan.FromSeconds(30));
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        var filteredBlobs = allBlobs.AsEnumerable();

        if (!string.IsNullOrEmpty(search))
        {
            var lowerSearch = search.ToLower();
            filteredBlobs = filteredBlobs.Where(b => b.Name.ToLower().Contains(lowerSearch));
        }

        if (!string.IsNullOrEmpty(status) && status != "Todos")
        {
            if (status == "Procesados") filteredBlobs = filteredBlobs.Where(b => b.Status == nameof(JobStatus.Completed));
            else if (status == "Pendientes") filteredBlobs = filteredBlobs.Where(b => b.Status is nameof(JobStatus.Pending) or nameof(JobStatus.Failed) or nameof(JobStatus.VectorSyncFailed));
            else if (status == "Encolados") filteredBlobs = filteredBlobs.Where(b => b.Status == nameof(JobStatus.Queued) || b.Status == nameof(JobStatus.Processing));
        }

        if (dateFrom.HasValue)
        {
            var df = dateFrom.Value.Date;
            filteredBlobs = filteredBlobs.Where(b => b.LastModified != null && b.LastModified.Value.Date >= df);
        }

        if (dateTo.HasValue)
        {
            var dt = dateTo.Value.Date;
            filteredBlobs = filteredBlobs.Where(b => b.LastModified != null && b.LastModified.Value.Date <= dt);
        }

        if (minSizeKb.HasValue)
        {
            var minBytes = minSizeKb.Value * 1024L;
            filteredBlobs = filteredBlobs.Where(b => b.Size >= minBytes);
        }

        if (maxSizeKb.HasValue)
        {
            var maxBytes = maxSizeKb.Value * 1024L;
            filteredBlobs = filteredBlobs.Where(b => b.Size <= maxBytes);
        }

        var totalItems = filteredBlobs.Count();
        var skip = ((page ?? 1) - 1) * (pageSize ?? 100);
        var pagedBlobs = filteredBlobs.Skip(skip).Take(pageSize ?? 100).ToList();

        return new BlobListResult
        {
            Total = totalItems,
            Page = page ?? 1,
            PageSize = pageSize ?? 100,
            Items = pagedBlobs,
            Stats = new BlobListStats
            {
                Total = allBlobs.Count,
                Pending = allBlobs.Count(b => b.Status is nameof(JobStatus.Pending) or nameof(JobStatus.Failed) or nameof(JobStatus.VectorSyncFailed)),
                Queued = allBlobs.Count(b => b.Status == nameof(JobStatus.Queued)),
                Completed = allBlobs.Count(b => b.Status == nameof(JobStatus.Completed)),
                Processing = allBlobs.Count(b => b.Status == nameof(JobStatus.Processing))
            }
        };
    }

    public async Task ResetDocumentStatusAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var jobState = await _dbContext.DocumentJobStates.FindAsync(new object[] { documentId }, cancellationToken);
        if (jobState != null)
        {
            jobState.Status = JobStatus.Pending;
            jobState.LastUpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            throw new KeyNotFoundException($"Documento {documentId} no encontrado.");
        }
    }

    public async Task<(int CompletedCount, int PendingCount)> ResetStuckProcessingDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var processedDocIds = await _dbContext.DocumentChunks
            .Select(c => c.DocumentId)
            .Distinct()
            .AsNoTracking().ToListAsync(cancellationToken);

        var completedCount = await _dbContext.DocumentJobStates
            .Where(j => j.Status == JobStatus.Processing && processedDocIds.Contains(j.DocumentId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.LastUpdatedAt, DateTime.UtcNow), cancellationToken);

        var pendingCount = await _dbContext.DocumentJobStates
            .Where(j => j.Status == JobStatus.Processing && !processedDocIds.Contains(j.DocumentId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Pending)
                .SetProperty(j => j.LastUpdatedAt, DateTime.UtcNow), cancellationToken);

        return (completedCount, pendingCount);
    }

    /// <summary>
    /// Removes the baseline pipeline data (chunks, job states) and its vector collection, which is
    /// recreated on the next ingestion. Previously this truncated <c>identity."DocumentChunks"</c>,
    /// a table that does not exist (the chunks live in the <c>ingestion</c> schema), so it always failed.
    /// </summary>
    public async Task ResetIngestionAsync(CancellationToken cancellationToken = default)
    {
        // TODO(audit-remediation 5.4): move the raw SQL behind an Infrastructure port.
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(
            _dbContext.Database,
            "TRUNCATE TABLE ingestion.\"DocumentChunks\"; TRUNCATE TABLE public.\"DocumentJobStates\";", cancellationToken);

        await _vectorStore.DropCollectionAsync(VectorCollection.DocumentChunks, cancellationToken);
        _cache.Remove($"blobs_list_{_bucketName}");
    }

    public Task<EnqueueResult> EnqueueBulkAsync(List<ProcessBlobRequest> requests, string? pipelineMode, CancellationToken cancellationToken = default)
    {
        var documents = requests
            .Select(r => ToMessage(r.BlobPath, r.Source))
            .OfType<DocumentMessage>()
            .ToList();

        return EnqueueAsync(documents, IngestionPipelinesParser.Parse(pipelineMode), cancellationToken);
    }

    public async Task<EnqueueResult> ReprocessAllAsync(string? pipelineMode, CancellationToken cancellationToken = default)
    {
        var pipelines = IngestionPipelinesParser.Parse(pipelineMode);
        var total = new EnqueueResult(0, 0);
        string? continuationToken = null;

        do
        {
            var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = "json/",
                ContinuationToken = continuationToken
            }, cancellationToken);

            if (response?.S3Objects is { Count: > 0 } objects)
            {
                var documents = objects
                    .Select(o => ToMessage(o.Key, source: null))
                    .OfType<DocumentMessage>()
                    .ToList();

                var page = await EnqueueAsync(documents, pipelines, cancellationToken);
                total = new EnqueueResult(total.Enqueued + page.Enqueued, total.Failed + page.Failed);
            }

            continuationToken = response?.NextContinuationToken;
        } while (!string.IsNullOrEmpty(continuationToken));

        return total;
    }

    /// <summary>
    /// Persists <see cref="JobStatus.Queued"/> first, then publishes (confirmed by the broker). Documents whose
    /// publish fails go back to <see cref="JobStatus.Pending"/>, so the state never claims work that is not queued.
    /// </summary>
    private async Task<EnqueueResult> EnqueueAsync(IReadOnlyList<DocumentMessage> documents, IngestionPipelines pipelines, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return new EnqueueResult(0, 0);
        }

        var docIds = documents.Select(d => d.DocumentId).Distinct().ToList();
        var jobStates = await _dbContext.DocumentJobStates
            .Where(j => docIds.Contains(j.DocumentId))
            .ToDictionaryAsync(j => j.DocumentId, cancellationToken);

        foreach (var docId in docIds)
        {
            if (!jobStates.TryGetValue(docId, out var jobState))
            {
                jobState = new DocumentJobState { DocumentId = docId, CreatedAt = DateTime.UtcNow };
                _dbContext.DocumentJobStates.Add(jobState);
                jobStates[docId] = jobState;
            }

            jobState.Status = JobStatus.Queued;
            jobState.ErrorMessage = null;
            jobState.LastUpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var failed = new List<string>();
        foreach (var document in documents)
        {
            try
            {
                await _publisher.PublishAsync(document, pipelines, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Publish failed for document {DocumentId}; reverting it to Pending", document.DocumentId);
                failed.Add(document.DocumentId);
            }
        }

        foreach (var docId in failed.Distinct())
        {
            var jobState = jobStates[docId];
            jobState.Status = JobStatus.Pending;
            jobState.ErrorMessage = "No se pudo encolar el documento.";
            jobState.LastUpdatedAt = DateTime.UtcNow;
        }

        if (failed.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Notify after the state is persisted, so the UI never shows a state that was rolled back.
        foreach (var docId in docIds)
        {
            await _notificationService.NotifyDocumentStatusChangedAsync(docId, jobStates[docId].Status.ToString());
        }

        _cache.Remove($"blobs_list_{_bucketName}");
        _dbContext.ChangeTracker.Clear();

        var failedCount = failed.Distinct().Count();
        return new EnqueueResult(docIds.Count - failedCount, failedCount);
    }

    /// <summary>Builds the queue message for a blob under <c>json/{SOURCE}/{documentId}.json</c>.</summary>
    private static DocumentMessage? ToMessage(string? blobPath, string? source)
    {
        if (string.IsNullOrEmpty(blobPath)) return null;

        var parts = blobPath.Split('/');
        var docId = parts.LastOrDefault()?.Replace(".json", "") ?? "";
        if (string.IsNullOrEmpty(docId)) return null;

        var inferredSource = parts.Length > 2 ? parts[^2] : (parts.Length == 2 ? parts[0] : "S3");
        if (blobPath.StartsWith("json/") && parts.Length >= 3)
        {
            inferredSource = parts[1];
        }

        return new DocumentMessage
        {
            Source = !string.IsNullOrEmpty(source) ? source : inferredSource,
            DocumentId = docId,
            BlobPath = blobPath
        };
    }
}
