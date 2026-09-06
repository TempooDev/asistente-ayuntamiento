using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AsistenteAyuntamiento.ApiService.Features.Admin;

public static class MigrationEndpoints
{
    public static void MapMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/migration"); // .RequireAuthorization(); // Add auth later if needed

        group.MapPost("/migrate-to-qdrant", async (
            IAppDbContext dbContext,
            QdrantClient qdrantClient) =>
        {
            var collectionName = "child_fragments";
            
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
                var fragments = await dbContext.ChildFragments
                    .AsNoTracking()
                    .Where(f => f.Id > lastId && f.Embedding != null)
                    .OrderBy(f => f.Id)
                    .Take(batchSize)
                    .ToListAsync();

                if (!fragments.Any())
                {
                    break;
                }

                var points = fragments.Select(f => new PointStruct
                {
                    Id = new PointId { Num = (ulong)f.Id },
                    Vectors = f.Embedding!.ToArray(),
                    Payload =
                    {
                        ["ParentId"] = f.ParentId,
                        ["Bulletin"] = (int)f.Bulletin,
                        ["Municipality"] = f.Municipality ?? "",
                        ["SubSection"] = f.SubSection ?? "",
                        ["ChunkText"] = f.ChunkText
                    }
                }).ToList();

                await qdrantClient.UpsertAsync(collectionName, points);

                totalMigrated += fragments.Count;
                lastId = fragments.Last().Id;
            }

            // Count in Qdrant to verify
            var qdrantCount = await qdrantClient.CountAsync(collectionName);

            return Results.Ok(new
            {
                Status = "Success",
                Migrated = totalMigrated,
                QdrantCount = qdrantCount
            });
        })
        .WithName("MigrateToQdrant")
        .WithSummary("Migrates vector embeddings from Postgres to Qdrant");
    }
}
