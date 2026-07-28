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
    }
}

public class ProcessBlobRequest
{
    public string BlobPath { get; set; } = string.Empty;
    public string Source { get; set; } = "Unknown";
}
