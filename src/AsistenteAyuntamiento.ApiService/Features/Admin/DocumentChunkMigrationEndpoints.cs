using AsistenteAyuntamiento.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AsistenteAyuntamiento.ApiService.Features.Admin;

public static class DocumentChunkMigrationEndpoints
{
    public static void MapDocumentChunkMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/migration/migrate-chunks-to-qdrant", async (
            AppDbContext dbContext,
            QdrantClient qdrantClient) =>
        {
            var collectionName = "document_chunks";
            
            // Re-create collection
            try
            {
                await qdrantClient.DeleteCollectionAsync(collectionName);
            }
            catch
            {
                // Ignore if it doesn't exist
            }
            
            await qdrantClient.CreateCollectionAsync(
                collectionName,
                new VectorParams { Size = 4096, Distance = Distance.Cosine }
            );

            // Fetch records from postgres in batches
            int batchSize = 100;
            long lastId = 0;
            int totalMigrated = 0;

            while (true)
            {
                var chunks = await dbContext.DocumentChunks
                    .AsNoTracking()
                    .Where(c => c.Id > lastId && c.Embedding != null)
                    .OrderBy(c => c.Id)
                    .Take(batchSize)
                    .ToListAsync();

                if (!chunks.Any())
                {
                    break;
                }

                var points = chunks.Select(c => new PointStruct
                {
                    Id = new PointId { Num = (ulong)c.Id },
                    Vectors = c.Embedding!.ToArray(),
                    Payload =
                    {
                        ["DocumentId"] = c.DocumentId,
                        ["Source"] = c.Source,
                        ["Title"] = c.Title ?? "",
                        ["Department"] = c.Department ?? "",
                        ["Content"] = c.Content,
                        ["ChunkIndex"] = c.ChunkIndex,
                        ["PublicationDate"] = c.PublicationDate.ToString("O")
                    }
                }).ToList();

                await qdrantClient.UpsertAsync(collectionName, points);

                totalMigrated += chunks.Count;
                lastId = chunks.Last().Id;
            }

            var qdrantCount = await qdrantClient.CountAsync(collectionName);

            return Results.Ok(new
            {
                Status = "Success",
                Migrated = totalMigrated,
                QdrantCount = qdrantCount
            });
        })
        .WithName("MigrateChunksToQdrant")
        .WithSummary("Migrates vector embeddings from DocumentChunks to Qdrant");
    }
}
