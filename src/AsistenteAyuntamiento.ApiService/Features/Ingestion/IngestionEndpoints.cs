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
        .WithName("ProcessBlobManually")
        .WithOpenApi();
        group.MapGet("/blobs", async (
            Azure.Storage.Blobs.BlobServiceClient blobServiceClient,
            AsistenteAyuntamiento.ApiService.Infrastructure.Data.AppDbContext dbContext) =>
        {
            var containerClient = blobServiceClient.GetBlobContainerClient("boletines");
            
            if (!await containerClient.ExistsAsync())
            {
                return Results.Ok(new System.Collections.Generic.List<object>());
            }
            
            var processedSources = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                System.Linq.Queryable.Distinct(System.Linq.Queryable.Select(dbContext.DocumentChunks, c => c.Source))
            );

            var blobs = new System.Collections.Generic.List<object>();
            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                blobs.Add(new {
                    Name = blobItem.Name,
                    Size = blobItem.Properties.ContentLength,
                    LastModified = blobItem.Properties.LastModified,
                    IsProcessed = processedSources.Contains(blobItem.Name)
                });
            }
            return Results.Ok(blobs);
        })
        .WithName("ListBlobs")
        .WithOpenApi();
    }
}

public class ProcessBlobRequest
{
    public string BlobPath { get; set; } = string.Empty;
    public string Source { get; set; } = "Unknown";
}
