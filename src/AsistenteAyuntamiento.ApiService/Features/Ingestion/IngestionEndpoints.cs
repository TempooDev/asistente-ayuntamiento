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
            Microsoft.Extensions.Configuration.IConfiguration config,
            AsistenteAyuntamiento.ApiService.Infrastructure.Data.AppDbContext dbContext) =>
        {
            var bucketName = config["Blob:BucketName"] ?? "boletines";
            
            var processedDocIds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                System.Linq.Queryable.Distinct(System.Linq.Queryable.Select(dbContext.DocumentChunks, c => c.DocumentId))
            );

            var blobs = new System.Collections.Generic.List<object>();
            
            try 
            {
                var request = new Amazon.S3.Model.ListObjectsV2Request
                {
                    BucketName = bucketName,
                    Prefix = "json/"
                };
                
                var response = await s3Client.ListObjectsV2Async(request);
                
                foreach (var s3Obj in response.S3Objects)
                {
                    var parts = s3Obj.Key.Split('/');
                    var docId = parts.LastOrDefault()?.Replace(".json", "") ?? "";
                    
                    blobs.Add(new {
                        Name = s3Obj.Key,
                        Size = s3Obj.Size,
                        LastModified = s3Obj.LastModified,
                        IsProcessed = processedDocIds.Contains(docId)
                    });
                }
            } 
            catch (Amazon.S3.AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                // Bucket not created yet
            }

            return Results.Ok(blobs);
        })
        .WithName("ListBlobs");
    }
}

public class ProcessBlobRequest
{
    public string BlobPath { get; set; } = string.Empty;
    public string Source { get; set; } = "Unknown";
}
