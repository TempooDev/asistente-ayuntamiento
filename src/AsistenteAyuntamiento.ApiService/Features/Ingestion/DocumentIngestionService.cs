using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Text;
using System.Text.Json;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
#pragma warning disable SKEXP0001
using Microsoft.SemanticKernel.Embeddings;

namespace AsistenteAyuntamiento.ApiService.Features.Ingestion;

public class DocumentIngestionService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly AppDbContext _dbContext;
    private readonly Kernel _kernel;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(BlobServiceClient blobServiceClient, AppDbContext dbContext, Kernel kernel, ILogger<DocumentIngestionService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _dbContext = dbContext;
        _kernel = kernel;
        _logger = logger;
    }

    public async Task ProcessBlobAsync(string blobPath, string source, CancellationToken cancellationToken = default)
    {
        // 1. Descargar JSON desde Azurite
        var containerClient = _blobServiceClient.GetBlobContainerClient("boletines");
        var blobClient = containerClient.GetBlobClient(blobPath);

        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            throw new Exception($"El blob {blobPath} no existe en Azurite.");
        }

        var downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
        var jsonContent = downloadResult.Value.Content.ToString();
        _logger.LogInformation($"JSON descargado ({jsonContent.Length} caracteres). Deserializando...");
        
        var document = JsonSerializer.Deserialize<ScrapedDocument>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (document == null)
        {
            _logger.LogWarning($"El blob {blobPath} no se pudo deserializar (document es null).");
            return;
        }
        
        _logger.LogInformation($"DocumentId deserializado: '{document.DocumentId}', Longitud del texto: {document.Content?.Length ?? 0}");

        // 2. Chunking
        List<string> paragraphs;
        if (string.IsNullOrWhiteSpace(document.Content))
        {
            _logger.LogInformation($"El blob {blobPath} tiene texto vacío. Se guardará un chunk con sus metadatos.");
            // Usar el título como contenido para que el motor vectorial pueda encontrarlo semánticamente
            var title = !string.IsNullOrWhiteSpace(document.Metadata?.Title) ? document.Metadata.Title : "Documento sin contenido";
            paragraphs = new List<string> { title };
        }
        else
        {
#pragma warning disable SKEXP0050
            paragraphs = TextChunker.SplitPlainTextParagraphs(
                TextChunker.SplitPlainTextLines(document.Content, 200),
                1000,
                100 // overlap
            );
#pragma warning restore SKEXP0050
        }

        // 3. Obtener servicio de embeddings
#pragma warning disable CS0618
        var embeddingGenerator = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
#pragma warning restore CS0618

        var chunks = new List<DocumentChunk>();
        for (int i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i];
            
            // 4. Vectorización
            var embedding = await embeddingGenerator.GenerateEmbeddingAsync(p, _kernel, cancellationToken);
            
            var chunk = new DocumentChunk
            {
                DocumentId = document.DocumentId,
                Source = source,
                Title = document.Metadata?.Title ?? string.Empty,
                Department = document.Metadata?.Department ?? string.Empty,
                Content = p,
                ChunkIndex = i,
                PublicationDate = DateTime.TryParse(document.Metadata?.PublicationDate, out var date) ? date.ToUniversalTime() : DateTime.UtcNow,
                Embedding = new Pgvector.Vector(embedding.ToArray())
            };
            chunks.Add(chunk);
        }

        // 5. Persistencia transaccional
        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Opcional: Eliminar chunks antiguos si es un re-procesamiento
            var existing = await _dbContext.DocumentChunks.Where(c => c.DocumentId == document.DocumentId).ToListAsync(cancellationToken);
            if (existing.Any())
            {
                _dbContext.DocumentChunks.RemoveRange(existing);
            }

            await _dbContext.DocumentChunks.AddRangeAsync(chunks, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            
            _logger.LogInformation($"Documento {document.DocumentId} vectorizado exitosamente con {chunks.Count} chunks.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
#pragma warning restore SKEXP0001
