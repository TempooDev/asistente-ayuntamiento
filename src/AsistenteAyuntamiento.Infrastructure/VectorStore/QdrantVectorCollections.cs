using System.Collections.Concurrent;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AsistenteAyuntamiento.Infrastructure.VectorStore;

/// <summary>
/// Creates Qdrant collections on first write (HNSW, cosine) and validates the vector size of existing ones,
/// so a fresh environment needs no manual setup (spec: qdrant-vector-store).
/// </summary>
public sealed class QdrantVectorCollections(QdrantClient client, ILogger<QdrantVectorCollections> logger) : IVectorCollections
{
    private readonly ConcurrentDictionary<string, ulong> _verified = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task EnsureAsync(string collection, int vectorSize, CancellationToken cancellationToken = default)
    {
        var expected = (ulong)vectorSize;
        if (_verified.TryGetValue(collection, out var known))
        {
            ThrowIfMismatch(collection, known, expected);
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!await client.CollectionExistsAsync(collection, cancellationToken))
            {
                try
                {
                    await client.CreateCollectionAsync(collection, new VectorParams { Size = expected, Distance = Distance.Cosine }, cancellationToken: cancellationToken);
                    logger.LogInformation("Created Qdrant collection {Collection} (size {Size}, cosine)", collection, expected);
                }
                catch (RpcException ex) when (ex.StatusCode is StatusCode.AlreadyExists or StatusCode.InvalidArgument)
                {
                    // Another instance created it concurrently; validated below.
                }
            }

            var info = await client.GetCollectionInfoAsync(collection, cancellationToken);
            var actual = info.Config.Params.VectorsConfig.Params.Size;
            ThrowIfMismatch(collection, actual, expected);
            _verified[collection] = actual;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DropAsync(string collection, CancellationToken cancellationToken = default)
    {
        if (await client.CollectionExistsAsync(collection, cancellationToken))
        {
            await client.DeleteCollectionAsync(collection, cancellationToken: cancellationToken);
            logger.LogWarning("Dropped Qdrant collection {Collection}", collection);
        }

        _verified.TryRemove(collection, out _);
    }

    private static void ThrowIfMismatch(string collection, ulong actual, ulong expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Qdrant collection '{collection}' has vector size {actual}, but the embedding model produces {expected}. " +
                "Recreate the collection (reset ingestion) after changing the embedding model.");
        }
    }
}
