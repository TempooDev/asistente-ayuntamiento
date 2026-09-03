using Microsoft.AspNetCore.Mvc;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AsistenteAyuntamiento.ApiService.Features.Ingestion.DTOs;
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
            [FromServices] DocumentIngestionService ingestionService,
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
            [FromServices] Infrastructure.Data.AppDbContext dbContext) =>
        {
            var bucketName = config["Blob:BucketName"] ?? "boletines";

            var processedDocIds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Queryable.Distinct(Queryable.Select(dbContext.DocumentChunks, c => c.DocumentId))
            );

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

                            var isProcessed = processedDocIds != null && processedDocIds.Contains(docId);
                            var objStatus = jobStates.TryGetValue(docId, out var jobStatus)
                                ? jobStatus
                                : (isProcessed ? "Completed" : "Pending");

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
                    
                    request.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated);
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
                    filteredBlobs = filteredBlobs.Where(b => b.Status == "Pending" || b.Status == "Failed" || b.Status == "Processing");
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
                filteredBlobs = filteredBlobs.Where(b => (b.Size / 1024) >= minSizeKb.Value);
            }

            if (maxSizeKb.HasValue)
            {
                filteredBlobs = filteredBlobs.Where(b => (b.Size / 1024) <= maxSizeKb.Value);
            }

            var finalBlobs = filteredBlobs.OrderByDescending(b => b.LastModified).ToList();
            
            int p = page ?? 1;
            int ps = pageSize ?? 20;

            var paged = finalBlobs
                .Skip((p - 1) * ps)
                .Take(ps)
                .ToList();

            return Results.Ok(new {
                Items = paged,
                TotalCount = finalBlobs.Count,
                Stats = new {
                    Total = totalCount,
                    Pending = pendingCount,
                    Processing = processingCount,
                    Completed = completedCount
                }
            });
        })
        .WithName("ListBlobs");

        group.MapPost("/reset-status/{documentId}", async (
            string documentId,
            [FromServices] Infrastructure.Data.AppDbContext dbContext,
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
            [FromServices] Infrastructure.Data.AppDbContext dbContext,
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
            [FromServices] Infrastructure.Data.AppDbContext dbContext,
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

        group.MapPost("/reprocess-all", async (
            [FromServices] Amazon.S3.IAmazonS3 s3Client,
            [FromServices] IConfiguration config,
            [FromServices] RabbitMQ.Client.IConnectionFactory connectionFactory,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            var bucketName = config["Blob:BucketName"] ?? "boletines";

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
