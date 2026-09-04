using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.ApiService.Features.Ingestion;

public static class IngestionEndpoints
{
    public static void MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingestion")
                       .WithTags("Ingestion");

        group.MapPost("/process-blob", async (
            [FromBody] ProcessBlobRequest request,
            [FromServices] IDocumentIngestionService ingestionService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation($"Iniciando proceso manual de {request.BlobPath} (Source: {request.Source})");
                await ingestionService.ProcessBlobAsync(request.BlobPath, request.Source);
                return Results.Ok(new { message = $"Blob {request.BlobPath} procesado y vectorizado correctamente." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error procesando blob manualmente");
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ProcessBlobManually");
                        group.MapGet("/blobs", async (
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? status,
            [FromQuery] string? search,
            [FromQuery] DateTime? dateFrom,
            [FromQuery] DateTime? dateTo,
            [FromQuery] int? minSizeKb,
            [FromQuery] int? maxSizeKb,
            [FromServices] Amazon.S3.IAmazonS3 s3Client,
            [FromServices] IConfiguration config,
            [FromServices] IAppDbContext dbContext) =>
        {
            var bucketName = config["Blob:BucketName"] ?? AsistenteAyuntamiento.Domain.Common.AppConstants.BlobStorage.DefaultBucketName;

            var jobStates = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
                dbContext.DocumentJobStates,
                j => j.DocumentId,
                j => j.Status
            );

            var allBlobs = new List<dynamic>();

            try
            {
                if (s3Client == null)
                {
                    return Results.Problem("S3 Client no está configurado correctamente.");
                }

                var request = new Amazon.S3.Model.ListObjectsV2Request
                {
                    BucketName = bucketName,
                    Prefix = "json/"
                };

                Amazon.S3.Model.ListObjectsV2Response response;
                do
                {
                    response = await s3Client.ListObjectsV2Async(request);

                    if (response?.S3Objects != null)
                    {
                        foreach (var s3Obj in response.S3Objects)
                        {
                            if (s3Obj?.Key == null) continue;

                            var parts = s3Obj.Key.Split('/');
                            var docId = parts.LastOrDefault()?.Replace(".json", "") ?? "";

                            var objStatus = jobStates.TryGetValue(docId, out var jobStatus)
                                ? jobStatus
                                : "Pending";

                            allBlobs.Add(new
                            {
                                Name = s3Obj.Key,
                                Size = s3Obj.Size,
                                LastModified = s3Obj.LastModified,
                                IsProcessed = objStatus == "Completed",
                                Status = objStatus
                            });
                        }
                    }
                    
                    request.ContinuationToken = response?.NextContinuationToken;
                } while (response?.IsTruncated == true);
            }
            catch (Amazon.S3.AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                // Bucket not created yet
            }

            int pendingCount = allBlobs.Count(b => b.Status == "Pending" || b.Status == "Failed");
            int processingCount = allBlobs.Count(b => b.Status == "Processing");
            int completedCount = allBlobs.Count(b => b.Status == "Completed");
            int totalCount = allBlobs.Count;

            var filteredBlobs = allBlobs.AsEnumerable();

            if (!string.IsNullOrEmpty(search))
            {
                var lowerSearch = search.ToLower();
                filteredBlobs = filteredBlobs.Where(b => ((string)b.Name).ToLower().Contains(lowerSearch));
            }

            if (!string.IsNullOrEmpty(status) && status != "Todos")
            {
                if (status == "Procesados")
                    filteredBlobs = filteredBlobs.Where(b => b.Status == "Completed");
                else if (status == "Pendientes")
                    filteredBlobs = filteredBlobs.Where(b => b.Status == "Pending" || b.Status == "Failed");
                else if (status == "Encolados")
                    filteredBlobs = filteredBlobs.Where(b => b.Status == "Queued" || b.Status == "Processing");
            }

            if (dateFrom.HasValue)
            {
                var df = dateFrom.Value.Date;
                filteredBlobs = filteredBlobs.Where(b => b.LastModified != null && ((DateTime)b.LastModified).Date >= df);
            }

            if (dateTo.HasValue)
            {
                var dt = dateTo.Value.Date;
                filteredBlobs = filteredBlobs.Where(b => b.LastModified != null && ((DateTime)b.LastModified).Date <= dt);
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

            // Paginación
            var skip = ((page ?? 1) - 1) * (pageSize ?? 100);
            var pagedBlobs = filteredBlobs.Skip(skip).Take(pageSize ?? 100).ToList();

            var result = new
            {
                Total = totalItems,
                Page = page ?? 1,
                PageSize = pageSize ?? 100,
                Items = pagedBlobs,
                Stats = new
                {
                    Total = allBlobs.Count,
                    Pending = allBlobs.Count(b => b.Status == "Pending" || b.Status == "Failed"),
                    Queued = allBlobs.Count(b => b.Status == "Queued"),
                    Completed = allBlobs.Count(b => b.Status == "Completed"),
                    Processing = allBlobs.Count(b => b.Status == "Processing")
                }
            };
            return Results.Ok(result);
        })
        .WithName("ListBlobs");

        group.MapPost("/reset-status/{documentId}", async (
            string documentId,
            [FromServices] IAppDbContext dbContext,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation($"Restableciendo estado del documento {documentId} a Pending...");
                
                var jobState = await dbContext.DocumentJobStates.FindAsync(documentId);
                if (jobState != null)
                {
                    jobState.Status = "Pending";
                    jobState.LastUpdatedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync();
                    return Results.Ok(new { message = $"El estado del documento {documentId} ha sido reiniciado a 'Pending'." });
                }
                
                return Results.NotFound(new { message = $"Documento {documentId} no encontrado." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error al reiniciar el estado del documento {documentId}");
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ResetDocumentStatus");

        group.MapPost("/reset-stuck-processing", async (
            [FromServices] IAppDbContext dbContext,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation("Corrigiendo documentos atascados en 'Processing'...");
                
                var processedDocIds = await dbContext.DocumentChunks
                    .Select(c => c.DocumentId)
                    .Distinct()
                    .ToListAsync();
                    
                var stuckJobs = await dbContext.DocumentJobStates
                    .Where(j => j.Status == "Processing")
                    .ToListAsync();
                    
                int completedCount = 0;
                int pendingCount = 0;
                    
                foreach (var job in stuckJobs)
                {
                    if (processedDocIds.Contains(job.DocumentId))
                    {
                        job.Status = "Completed";
                        completedCount++;
                    }
                    else
                    {
                        job.Status = "Pending";
                        pendingCount++;
                    }
                    job.LastUpdatedAt = DateTime.UtcNow;
                }
                
                await dbContext.SaveChangesAsync();
                
                return Results.Ok(new { message = $"Se han marcado {completedCount} documentos como 'Completed' (ya vectorizados) y reiniciado {pendingCount} a 'Pending'." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al reiniciar documentos atascados");
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ResetStuckProcessingDocuments");

        group.MapPost("/reset", async (
            [FromServices] IAppDbContext dbContext,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation("Restableciendo la base de datos de vectores y estados...");
                
                // Truncate vector database and job states using raw SQL
                await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(
                    dbContext.Database, 
                    "TRUNCATE TABLE identity.\"DocumentChunks\"; TRUNCATE TABLE public.\"DocumentJobStates\";");
                
                return Results.Ok(new { message = "Todos los documentos han sido eliminados de la base de datos de vectores. RabbitMQ los volverá a procesar al reiniciar o reenviar los mensajes." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al reiniciar la base de datos de documentos");
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ResetIngestion");


        group.MapPost("/enqueue-bulk", async (
            [FromBody] List<AsistenteAyuntamiento.Application.Features.Ingestion.DTOs.ProcessBlobRequest> requests,
            [FromServices] RabbitMQ.Client.IConnectionFactory connectionFactory,
            [FromServices] IAppDbContext dbContext,
            [FromServices] ILoggerFactory loggerFactory,
            [FromServices] INotificationService notificationService) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation($"Encolando {requests.Count} documentos...");

                using var connection = await connectionFactory.CreateConnectionAsync();
                using var channel = await connection.CreateChannelAsync();
                await channel.QueueDeclareAsync("documents_to_process", durable: true, exclusive: false, autoDelete: false, arguments: null);

                int count = 0;
                foreach (var req in requests)
                {
                    var docId = req.BlobPath.Split('/').LastOrDefault()?.Replace(".json", "") ?? "";
                    
                    var message = new
                    {
                        source = req.Source ?? "S3",
                        document_id = docId,
                        blob_path = req.BlobPath
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(message);
                    var body = System.Text.Encoding.UTF8.GetBytes(json);

                    await channel.BasicPublishAsync(
                        exchange: string.Empty,
                        routingKey: "documents_to_process",
                        mandatory: false,
                        basicProperties: new RabbitMQ.Client.BasicProperties(),
                        body: body);
                    
                    // Update job state
                    var jobState = await dbContext.DocumentJobStates.FindAsync(docId);
                    if (jobState != null)
                    {
                        jobState.Status = "Queued";
                        jobState.LastUpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        dbContext.DocumentJobStates.Add(new DocumentJobState
                        {
                            DocumentId = docId,
                            Status = "Queued",
                            CreatedAt = DateTime.UtcNow,
                            LastUpdatedAt = DateTime.UtcNow
                        });
                    }

                    await notificationService.NotifyDocumentStatusChangedAsync(docId, "Queued");
                    count++;
                }

                await dbContext.SaveChangesAsync();

                return Results.Ok(new { message = $"Se han encolado {count} documentos en RabbitMQ." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al encolar documentos");
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("EnqueueBulkBlobs");

        group.MapPost("/reprocess-all", async (
            [FromServices] Amazon.S3.IAmazonS3 s3Client,
            [FromServices] IConfiguration config,
            [FromServices] RabbitMQ.Client.IConnectionFactory connectionFactory,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            var bucketName = config["Blob:BucketName"] ?? AsistenteAyuntamiento.Domain.Common.AppConstants.BlobStorage.DefaultBucketName;

            try
            {
                logger.LogInformation("Iniciando reprocesado masivo de todos los documentos en S3...");
                
                var request = new Amazon.S3.Model.ListObjectsV2Request
                {
                    BucketName = bucketName,
                    Prefix = "json/"
                };
                var response = await s3Client.ListObjectsV2Async(request);

                if (response?.S3Objects == null || response.S3Objects.Count == 0)
                {
                    return Results.Ok(new { message = "No se encontraron documentos en S3." });
                }

                using var connection = await connectionFactory.CreateConnectionAsync();
                using var channel = await connection.CreateChannelAsync();
                await channel.QueueDeclareAsync("documents_to_process", durable: true, exclusive: false, autoDelete: false, arguments: null);

                int count = 0;
                foreach (var s3Obj in response.S3Objects)
                {
                    if (string.IsNullOrEmpty(s3Obj.Key)) continue;

                    var docId = s3Obj.Key.Split('/').LastOrDefault()?.Replace(".json", "") ?? "";
                    
                    // Reusing the DocumentMessage structure directly inline or anonymously isn't possible because we need strict schema
                    // Since DocumentMessage is in another file, let's create it anonymously and serialize
                    var message = new
                    {
                        source = "S3",
                        document_id = docId,
                        blob_path = s3Obj.Key
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(message);
                    var body = System.Text.Encoding.UTF8.GetBytes(json);

                    await channel.BasicPublishAsync(
                        exchange: string.Empty,
                        routingKey: "documents_to_process",
                        mandatory: false,
                        basicProperties: new RabbitMQ.Client.BasicProperties(),
                        body: body);
                    
                    count++;
                }

                return Results.Ok(new { message = $"Se han encolado {count} documentos para reprocesado masivo en RabbitMQ." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al encolar el reprocesado masivo");
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ReprocessAllBlobs");
    }
}
