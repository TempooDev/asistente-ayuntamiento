using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.ApiService.Features.Ingestion;

public static class IngestionEndpoints
{
    public static void MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingestion")
                       .WithTags("Ingestion");

        group.MapPost("/process-blob", async (
            [FromBody] ProcessBlobRequest request,
            DocumentIngestionService ingestionService,
            ILoggerFactory loggerFactory) =>
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
            Amazon.S3.IAmazonS3 s3Client,
            IConfiguration config,
            Infrastructure.Data.AppDbContext dbContext) =>
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

            var blobs = new List<object>();

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

                var response = await s3Client.ListObjectsV2Async(request);

                if (response?.S3Objects != null)
                {
                    foreach (var s3Obj in response.S3Objects)
                    {
                        if (s3Obj?.Key == null) continue;

                        var parts = s3Obj.Key.Split('/');
                        var docId = parts.LastOrDefault()?.Replace(".json", "") ?? "";

                        var isProcessed = processedDocIds != null && processedDocIds.Contains(docId);
                        var status = jobStates.TryGetValue(docId, out var jobStatus)
                            ? jobStatus
                            : (isProcessed ? "Completed" : "Pending");

                        blobs.Add(new
                        {
                            Name = s3Obj.Key,
                            Size = s3Obj.Size,
                            LastModified = s3Obj.LastModified,
                            IsProcessed = status == "Completed",
                            Status = status
                        });
                    }
                }
            }
            catch (Amazon.S3.AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                // Bucket not created yet
            }

            return Results.Ok(blobs);
        })
        .WithName("ListBlobs");

        group.MapPost("/reset", async (
            Infrastructure.Data.AppDbContext dbContext,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation("Restableciendo la base de datos de vectores y estados...");
                
                // Truncate vector database and job states using raw SQL
                await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(
                    dbContext.Database, 
                    "TRUNCATE TABLE public.\"DocumentChunks\"; TRUNCATE TABLE public.\"DocumentJobStates\";");
                
                return Results.Ok(new { message = "Todos los documentos han sido eliminados de la base de datos de vectores. RabbitMQ los volverá a procesar al reiniciar o reenviar los mensajes." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al reiniciar la base de datos de documentos");
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ResetIngestion");
    }
}

public class ProcessBlobRequest
{
    public string BlobPath { get; set; } = string.Empty;
    public string Source { get; set; } = "Unknown";
}
