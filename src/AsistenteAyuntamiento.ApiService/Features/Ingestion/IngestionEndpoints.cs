using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using Microsoft.AspNetCore.Mvc;

namespace AsistenteAyuntamiento.ApiService.Features.Ingestion;

public static class IngestionEndpoints
{
    public static void MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingestion")
            .RequireAuthorization()
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
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                var result = await adminService.ListBlobsAsync(page, pageSize, status, search, dateFrom, dateTo, minSizeKb, maxSizeKb);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching blobs");
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ListBlobs");

        group.MapPost("/reset-status/{documentId}", async (
            string documentId,
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation($"Restableciendo estado del documento {documentId} a Pending...");
                await adminService.ResetDocumentStatusAsync(documentId);
                return Results.Ok(new { message = $"El estado del documento {documentId} ha sido reiniciado a 'Pending'." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error al reiniciar el estado del documento {documentId}");
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ResetDocumentStatus");

        group.MapPost("/reset-stuck-processing", async (
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation("Corrigiendo documentos atascados en 'Processing'...");
                var (completedCount, pendingCount) = await adminService.ResetStuckProcessingDocumentsAsync();
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
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation("Restableciendo la base de datos de vectores y estados...");
                await adminService.ResetIngestionAsync();
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
            [FromQuery] string? pipelineMode,
            [FromBody] List<ProcessBlobRequest> requests,
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation($"Encolando {requests.Count} documentos... (Mode: {pipelineMode ?? "BOTH"})");
                var count = await adminService.EnqueueBulkAsync(requests, pipelineMode);
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
            [FromQuery] string? pipelineMode,
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            try
            {
                logger.LogInformation($"Iniciando reprocesado masivo de todos los documentos en S3... (Mode: {pipelineMode ?? "BOTH"})");
                var count = await adminService.ReprocessAllAsync(pipelineMode);
                
                if (count == 0)
                {
                    return Results.Ok(new { message = "No se encontraron documentos en S3." });
                }
                
                return Results.Ok(new { message = $"Se han encolado {count} documentos en las colas seleccionadas para reprocesado masivo." });
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
