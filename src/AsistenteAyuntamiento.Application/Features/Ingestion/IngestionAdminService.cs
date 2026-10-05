using Amazon.S3;
using Amazon.S3.Model;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text.Json;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class IngestionAdminService : IIngestionAdminService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _config;
    private readonly IAppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IConnectionFactory _connectionFactory;
    private readonly INotificationService _notificationService;
    private readonly ILogger<IngestionAdminService> _logger;
    private readonly string _bucketName;
    private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

    public IngestionAdminService(
        IAmazonS3 s3Client,
        IConfiguration config,
        IAppDbContext dbContext,
        IMemoryCache cache,
        IConnectionFactory connectionFactory,
        INotificationService notificationService,
        ILogger<IngestionAdminService> logger)
    {
        _s3Client = s3Client;
        _config = config;
        _dbContext = dbContext;
        _cache = cache;
        _connectionFactory = connectionFactory;
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
                        .ToDictionaryAsync(j => j.DocumentId, j => j.Status, cancellationToken);

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
                                    var objStatus = jobStates.TryGetValue(docId, out var jobStatus) ? jobStatus : "Pending";

                                    allBlobs.Add(new BlobItemDto(s3Obj.Key, s3Obj.Size, s3Obj.LastModified, objStatus == "Completed", objStatus));
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
            if (status == "Procesados") filteredBlobs = filteredBlobs.Where(b => b.Status == "Completed");
            else if (status == "Pendientes") filteredBlobs = filteredBlobs.Where(b => b.Status == "Pending" || b.Status == "Failed");
            else if (status == "Encolados") filteredBlobs = filteredBlobs.Where(b => b.Status == "Queued" || b.Status == "Processing");
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
                Pending = allBlobs.Count(b => b.Status == "Pending" || b.Status == "Failed"),
                Queued = allBlobs.Count(b => b.Status == "Queued"),
                Completed = allBlobs.Count(b => b.Status == "Completed"),
                Processing = allBlobs.Count(b => b.Status == "Processing")
            }
        };
    }

    public async Task ResetDocumentStatusAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var jobState = await _dbContext.DocumentJobStates.FindAsync(new object[] { documentId }, cancellationToken);
        if (jobState != null)
        {
            jobState.Status = "Pending";
            jobState.LastUpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            throw new Exception($"Documento {documentId} no encontrado.");
        }
    }

    public async Task<(int CompletedCount, int PendingCount)> ResetStuckProcessingDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var processedDocIds = await _dbContext.DocumentChunks
            .Select(c => c.DocumentId)
            .Distinct()
            .AsNoTracking().ToListAsync(cancellationToken);

        var completedCount = await _dbContext.DocumentJobStates
            .Where(j => j.Status == "Processing" && processedDocIds.Contains(j.DocumentId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, "Completed")
                .SetProperty(j => j.LastUpdatedAt, DateTime.UtcNow), cancellationToken);

        var pendingCount = await _dbContext.DocumentJobStates
            .Where(j => j.Status == "Processing" && !processedDocIds.Contains(j.DocumentId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, "Pending")
                .SetProperty(j => j.LastUpdatedAt, DateTime.UtcNow), cancellationToken);

        return (completedCount, pendingCount);
    }

    public async Task ResetIngestionAsync(CancellationToken cancellationToken = default)
    {
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(
            _dbContext.Database,
            "TRUNCATE TABLE identity.\"DocumentChunks\"; TRUNCATE TABLE public.\"DocumentJobStates\";", cancellationToken);
    }

    public async Task<int> EnqueueBulkAsync(List<ProcessBlobRequest> requests, string? pipelineMode, CancellationToken cancellationToken = default)
    {
        var mode = string.IsNullOrEmpty(pipelineMode) ? "BOTH" : pipelineMode.ToUpper();
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        if (mode == "BASELINE" || mode == "BOTH")
            await channel.QueueDeclareAsync("documents_to_process_baseline", durable: true, exclusive: false, autoDelete: false, arguments: null);
        if (mode == "HIERARCHICAL" || mode == "BOTH")
            await channel.QueueDeclareAsync("documents_to_process_hierarchical", durable: true, exclusive: false, autoDelete: false, arguments: null);

        var docIds = requests
            .Select(r => r.BlobPath.Split('/').LastOrDefault()?.Replace(".json", "") ?? "")
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        var existingJobStates = await _dbContext.DocumentJobStates
            .Where(j => docIds.Contains(j.DocumentId))
            .ToDictionaryAsync(j => j.DocumentId, cancellationToken);

        int count = 0;
        foreach (var req in requests)
        {
            var parts = req.BlobPath.Split('/');
            var docId = parts.LastOrDefault()?.Replace(".json", "") ?? "";
            if (string.IsNullOrEmpty(docId)) continue;

            var inferredSource = parts.Length > 2 ? parts[parts.Length - 2] : (parts.Length == 2 ? parts[0] : "S3");
            if (req.BlobPath.StartsWith("json/") && parts.Length >= 3)
            {
                inferredSource = parts[1];
            }

            var message = new
            {
                source = !string.IsNullOrEmpty(req.Source) ? req.Source : inferredSource,
                document_id = docId,
                blob_path = req.BlobPath
            };

            var json = JsonSerializer.Serialize(message);
            var body = System.Text.Encoding.UTF8.GetBytes(json);

            if (mode == "BASELINE" || mode == "BOTH")
            {
                await channel.BasicPublishAsync(string.Empty, "documents_to_process_baseline", false, new RabbitMQ.Client.BasicProperties(), body);
            }
            if (mode == "HIERARCHICAL" || mode == "BOTH")
            {
                await channel.BasicPublishAsync(string.Empty, "documents_to_process_hierarchical", false, new RabbitMQ.Client.BasicProperties(), body);
            }

            if (existingJobStates.TryGetValue(docId, out var jobState))
            {
                jobState.Status = "Queued";
                jobState.LastUpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _dbContext.DocumentJobStates.Add(new DocumentJobState
                {
                    DocumentId = docId,
                    Status = "Queued",
                    CreatedAt = DateTime.UtcNow,
                    LastUpdatedAt = DateTime.UtcNow
                });
            }

            await _notificationService.NotifyDocumentStatusChangedAsync(docId, "Queued");
            count++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return count;
    }

    public async Task<int> ReprocessAllAsync(string? pipelineMode, CancellationToken cancellationToken = default)
    {
        var mode = string.IsNullOrEmpty(pipelineMode) ? "BOTH" : pipelineMode.ToUpper();
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        if (mode == "BASELINE" || mode == "BOTH")
            await channel.QueueDeclareAsync("documents_to_process_baseline", durable: true, exclusive: false, autoDelete: false, arguments: null);
        if (mode == "HIERARCHICAL" || mode == "BOTH")
            await channel.QueueDeclareAsync("documents_to_process_hierarchical", durable: true, exclusive: false, autoDelete: false, arguments: null);

        int count = 0;
        string? continuationToken = null;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = "json/",
                ContinuationToken = continuationToken
            };

            var response = await _s3Client.ListObjectsV2Async(request, cancellationToken);

            if (response?.S3Objects != null && response.S3Objects.Count > 0)
            {
                var batchDocIds = response.S3Objects
                    .Select(o => o.Key.Split('/').LastOrDefault()?.Replace(".json", "") ?? "")
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                var existingJobStates = await _dbContext.DocumentJobStates
                    .Where(j => batchDocIds.Contains(j.DocumentId))
                    .ToDictionaryAsync(j => j.DocumentId, cancellationToken);

                foreach (var s3Obj in response.S3Objects)
                {
                    if (string.IsNullOrEmpty(s3Obj.Key)) continue;

                    var parts = s3Obj.Key.Split('/');
                    var docId = parts.LastOrDefault()?.Replace(".json", "") ?? "";
                    if (string.IsNullOrEmpty(docId)) continue;

                    var inferredSource = parts.Length > 2 ? parts[parts.Length - 2] : (parts.Length == 2 ? parts[0] : "S3");
                    if (s3Obj.Key.StartsWith("json/") && parts.Length >= 3)
                    {
                        inferredSource = parts[1];
                    }

                    var message = new
                    {
                        source = inferredSource,
                        document_id = docId,
                        blob_path = s3Obj.Key
                    };

                    var json = JsonSerializer.Serialize(message);
                    var body = System.Text.Encoding.UTF8.GetBytes(json);

                    if (mode == "BASELINE" || mode == "BOTH")
                        await channel.BasicPublishAsync(string.Empty, "documents_to_process_baseline", false, new RabbitMQ.Client.BasicProperties(), body);
                    if (mode == "HIERARCHICAL" || mode == "BOTH")
                        await channel.BasicPublishAsync(string.Empty, "documents_to_process_hierarchical", false, new RabbitMQ.Client.BasicProperties(), body);

                    if (existingJobStates.TryGetValue(docId, out var jobState))
                    {
                        jobState.Status = "Queued";
                        jobState.LastUpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        _dbContext.DocumentJobStates.Add(new DocumentJobState
                        {
                            DocumentId = docId,
                            Status = "Queued",
                            CreatedAt = DateTime.UtcNow,
                            LastUpdatedAt = DateTime.UtcNow
                        });
                    }

                    await _notificationService.NotifyDocumentStatusChangedAsync(docId, "Queued");
                    count++;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                ((Microsoft.EntityFrameworkCore.DbContext)_dbContext).ChangeTracker.Clear();
            }

            continuationToken = response?.NextContinuationToken;

        } while (!string.IsNullOrEmpty(continuationToken));

        return count;
    }
}
