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

public class ProcessBlobRequest
{
    public string BlobPath { get; set; } = string.Empty;
    public string Source { get; set; } = "Unknown";
}
