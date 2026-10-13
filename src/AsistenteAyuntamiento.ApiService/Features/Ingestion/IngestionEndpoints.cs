using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.ApiService.Security;
using Microsoft.AspNetCore.Mvc;

namespace AsistenteAyuntamiento.ApiService.Features.Ingestion;

/// <summary>
/// Administrative ingestion endpoints. Unhandled errors are turned into generic ProblemDetails
/// by <c>UseExceptionHandler</c>, so no exception details reach the client.
/// </summary>
public static class IngestionEndpoints
{
    public static void MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingestion")
            .RequireAuthorization(Policies.Admin)
            .WithTags("Ingestion");

        group.MapPost("/process-blob", async (
            [FromBody] ProcessBlobRequest request,
            [FromServices] IDocumentIngestionService ingestionService,
            [FromServices] ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            logger.LogInformation("Iniciando proceso manual de {BlobPath} (Source: {Source})", request.BlobPath, request.Source);
            await ingestionService.ProcessBlobAsync(request.BlobPath, request.Source, cancellationToken);
            return Results.Ok(new { message = $"Blob {request.BlobPath} procesado y vectorizado correctamente." });
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
            [FromServices] IIngestionAdminService adminService, CancellationToken cancellationToken) =>
        {
            var result = await adminService.ListBlobsAsync(page, pageSize, status, search, dateFrom, dateTo, minSizeKb, maxSizeKb, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ListBlobs");

        group.MapPost("/reset-status/{documentId}", async (
            string documentId,
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            logger.LogInformation("Restableciendo estado del documento {DocumentId} a Pending", documentId);
            try
            {
                await adminService.ResetDocumentStatusAsync(documentId, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            return Results.Ok(new { message = $"El estado del documento {documentId} ha sido reiniciado a 'Pending'." });
        })
        .WithName("ResetDocumentStatus");

        group.MapPost("/reset-stuck-processing", async (
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            logger.LogInformation("Corrigiendo documentos atascados en 'Processing'");
            var (completedCount, pendingCount) = await adminService.ResetStuckProcessingDocumentsAsync(cancellationToken);
            return Results.Ok(new { message = $"Se han marcado {completedCount} documentos como 'Completed' (ya vectorizados) y reiniciado {pendingCount} a 'Pending'." });
        })
        .WithName("ResetStuckProcessingDocuments");

        group.MapPost("/reset", async (
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            logger.LogWarning("Restableciendo la base de datos de vectores y estados");
            await adminService.ResetIngestionAsync(cancellationToken);
            return Results.Ok(new { message = "Todos los documentos han sido eliminados de la base de datos de vectores. RabbitMQ los volverá a procesar al reiniciar o reenviar los mensajes." });
        })
        .WithName("ResetIngestion");

        group.MapPost("/enqueue-bulk", async (
            [FromQuery] string? pipelineMode,
            [FromBody] List<ProcessBlobRequest> requests,
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            logger.LogInformation("Encolando {Count} documentos (Mode: {PipelineMode})", requests.Count, pipelineMode ?? "BOTH");
            var result = await adminService.EnqueueBulkAsync(requests, pipelineMode, cancellationToken);
            return Results.Ok(new
            {
                message = result.Failed == 0
                    ? $"Se han encolado {result.Enqueued} documentos en RabbitMQ."
                    : $"Se han encolado {result.Enqueued} documentos; {result.Failed} no se pudieron encolar y siguen como 'Pending'.",
                enqueued = result.Enqueued,
                failed = result.Failed
            });
        })
        .WithName("EnqueueBulkBlobs");

        group.MapPost("/reprocess-all", async (
            [FromQuery] string? pipelineMode,
            [FromServices] IIngestionAdminService adminService,
            [FromServices] ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("IngestionEndpoints");
            logger.LogInformation("Iniciando reprocesado masivo de todos los documentos en S3 (Mode: {PipelineMode})", pipelineMode ?? "BOTH");
            var result = await adminService.ReprocessAllAsync(pipelineMode, cancellationToken);

            if (result.Enqueued == 0 && result.Failed == 0)
            {
                return Results.Ok(new { message = "No se encontraron documentos en S3.", enqueued = 0, failed = 0 });
            }

            return Results.Ok(new
            {
                message = result.Failed == 0
                    ? $"Se han encolado {result.Enqueued} documentos en las colas seleccionadas para reprocesado masivo."
                    : $"Se han encolado {result.Enqueued} documentos; {result.Failed} no se pudieron encolar y siguen como 'Pending'.",
                enqueued = result.Enqueued,
                failed = result.Failed
            });
        })
        .WithName("ReprocessAllBlobs");
    }
}
