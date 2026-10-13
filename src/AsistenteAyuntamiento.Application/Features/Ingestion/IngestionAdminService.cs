using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using AsistenteAyuntamiento.Application.Common.Messaging;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Common.Storage;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class IngestionAdminService : IIngestionAdminService
{
    private readonly IBlobStorage _blobStorage;
    private readonly IAppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IIngestionQueuePublisher _publisher;
    private readonly IVectorStore _vectorStore;
    private readonly IIngestionMaintenance _maintenance;
    private readonly INotificationService _notificationService;
    private readonly ILogger<IngestionAdminService> _logger;
    private const string BlobListCacheKey = "blobs_list";
    private const string ScrapedJsonPrefix = "json/";
    private const int ReprocessBatchSize = 1000;
    private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

    public IngestionAdminService(
        IBlobStorage blobStorage,
        IAppDbContext dbContext,
        IMemoryCache cache,
        IIngestionQueuePublisher publisher,
        IVectorStore vectorStore,
        IIngestionMaintenance maintenance,
        INotificationService notificationService,
        ILogger<IngestionAdminService> logger)
    {
        _blobStorage = blobStorage;
        _dbContext = dbContext;
        _cache = cache;
        _publisher = publisher;
        _vectorStore = vectorStore;
        _maintenance = maintenance;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<BlobListResult> ListBlobsAsync(int? page, int? pageSize, string? status, string? search, DateTime? dateFrom, DateTime? dateTo, int? minSizeKb, int? maxSizeKb, CancellationToken cancellationToken = default)
    {
        var cacheKey = BlobListCacheKey;
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
                        await foreach (var blob in _blobStorage.ListAsync(ScrapedJsonPrefix, cancellationToken))
                        {
                            var docId = blob.Key.Split('/').LastOrDefault()?.Replace(".json", "") ?? "";
                            var objStatus = jobStates.TryGetValue(docId, out var jobStatus) ? jobStatus : nameof(JobStatus.Pending);

                            allBlobs.Add(new BlobItemDto(blob.Key, blob.Size, blob.LastModified, objStatus == nameof(JobStatus.Completed), objStatus));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error listing objects from blob storage");
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
            jobState.Reset();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            throw new KeyNotFoundException($"Documento {documentId} no encontrado.");
        }
    }

    /// <summary>
    /// Documents left in Processing (a worker died): each stuck pipeline becomes Completed if its data exists
    /// (baseline chunks, hierarchical parent document) and Pending otherwise. Counts are by resulting summary.
    /// </summary>
    public async Task<(int CompletedCount, int PendingCount)> ResetStuckProcessingDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var stuck = await _dbContext.DocumentJobStates
            .Where(j => j.Status == JobStatus.Processing)
            .ToListAsync(cancellationToken);
        if (stuck.Count == 0)
        {
            return (0, 0);
        }

        var ids = stuck.Select(j => j.DocumentId).ToList();
        var withChunks = (await _dbContext.DocumentChunks.Where(c => ids.Contains(c.DocumentId)).Select(c => c.DocumentId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var withParents = (await _dbContext.ParentDocuments.Where(p => ids.Contains(p.DocumentId)).Select(p => p.DocumentId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        foreach (var state in stuck)
        {
            if (state.BaselineStatus == JobStatus.Processing)
            {
                state.SetPipelineStatus(PipelineType.Baseline, withChunks.Contains(state.DocumentId) ? JobStatus.Completed : JobStatus.Pending);
            }
            if (state.HierarchicalStatus == JobStatus.Processing)
            {
                state.SetPipelineStatus(PipelineType.Hierarchical, withParents.Contains(state.DocumentId) ? JobStatus.Completed : JobStatus.Pending);
            }
            if (state.Status == JobStatus.Processing)
            {
                // Rows from before per-pipeline statuses: decide on the baseline data, as before.
                state.SetPipelineStatus(PipelineType.Baseline, withChunks.Contains(state.DocumentId) ? JobStatus.Completed : JobStatus.Pending);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (stuck.Count(s => s.Status == JobStatus.Completed), stuck.Count(s => s.Status != JobStatus.Completed));
    }

    private static List<PipelineType> RequestedPipelines(IngestionPipelines pipelines)
    {
        var requested = new List<PipelineType>();
        if (pipelines.HasFlag(IngestionPipelines.Baseline)) requested.Add(PipelineType.Baseline);
        if (pipelines.HasFlag(IngestionPipelines.Hierarchical)) requested.Add(PipelineType.Hierarchical);
        return requested;
    }

    /// <summary>
    /// Removes the baseline pipeline data (chunks, job states) and its vector collection, which is
    /// recreated on the next ingestion. Previously this truncated <c>identity."DocumentChunks"</c>,
    /// a table that does not exist (the chunks live in the <c>ingestion</c> schema), so it always failed.
    /// </summary>
    public async Task ResetIngestionAsync(CancellationToken cancellationToken = default)
    {
        await _maintenance.ClearBaselineDataAsync(cancellationToken);

        await _vectorStore.DropCollectionAsync(VectorCollection.DocumentChunks, cancellationToken);
        _cache.Remove(BlobListCacheKey);
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
        var batch = new List<DocumentMessage>(ReprocessBatchSize);

        await foreach (var blob in _blobStorage.ListAsync(ScrapedJsonPrefix, cancellationToken))
        {
            if (ToMessage(blob.Key, source: null) is { } message)
            {
                batch.Add(message);
            }

            if (batch.Count == ReprocessBatchSize)
            {
                total = Add(total, await EnqueueAsync(batch, pipelines, cancellationToken));
                batch = new List<DocumentMessage>(ReprocessBatchSize);
            }
        }

        if (batch.Count > 0)
        {
            total = Add(total, await EnqueueAsync(batch, pipelines, cancellationToken));
        }

        return total;

        static EnqueueResult Add(EnqueueResult a, EnqueueResult b) => new(a.Enqueued + b.Enqueued, a.Failed + b.Failed);
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
        var requested = RequestedPipelines(pipelines);
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

            // Only the requested pipelines are queued; the other one keeps its own status.
            foreach (var pipeline in requested)
            {
                jobState.SetPipelineStatus(pipeline, JobStatus.Queued);
            }
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
            foreach (var pipeline in requested)
            {
                jobState.SetPipelineStatus(pipeline, JobStatus.Pending, "No se pudo encolar el documento.");
            }
        }

        if (failed.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Notify after the state is persisted, so the UI never shows a state that was rolled back; one batch for
        // the whole request instead of one message per document.
        await _notificationService.NotifyDocumentStatusesChangedAsync(
            docIds.Select(docId => new DocumentStatusChange(docId, jobStates[docId].Status.ToString())).ToList());

        _cache.Remove(BlobListCacheKey);
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
