using System.Collections.Concurrent;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace AsistenteAyuntamiento.Infrastructure.VectorStore;

/// <summary>
/// Qdrant implementation of <see cref="IVectorStore"/>. Creates collections on first write (HNSW, cosine) and
/// validates the vector size of existing ones, so a fresh environment needs no manual setup (spec: qdrant-vector-store).
/// </summary>
public sealed class QdrantVectorStore(
    QdrantClient client,
    IOptions<VectorStoreOptions> options,
    ILogger<QdrantVectorStore> logger) : IVectorStore, IDisposable
{
    private readonly ConcurrentDictionary<string, ulong> _verified = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly VectorStoreOptions _options = options.Value;

    public async Task EnsureCollectionAsync(VectorCollection collection, int vectorSize, CancellationToken cancellationToken = default)
    {
        var name = _options.NameOf(collection);
        var expected = (ulong)vectorSize;
        if (_verified.TryGetValue(name, out var known))
        {
            ThrowIfMismatch(name, known, expected);
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!await client.CollectionExistsAsync(name, cancellationToken))
            {
                try
                {
                    await client.CreateCollectionAsync(name, new VectorParams { Size = expected, Distance = Distance.Cosine }, cancellationToken: cancellationToken);
                    logger.LogInformation("Created Qdrant collection {Collection} (size {Size}, cosine)", name, expected);
                }
                catch (RpcException ex) when (ex.StatusCode is StatusCode.AlreadyExists or StatusCode.InvalidArgument)
                {
                    // Another instance created it concurrently; validated below.
                }
            }

            var info = await client.GetCollectionInfoAsync(name, cancellationToken);
            var actual = info.Config.Params.VectorsConfig.Params.Size;
            ThrowIfMismatch(name, actual, expected);
            _verified[name] = actual;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<bool> CollectionExistsAsync(VectorCollection collection, CancellationToken cancellationToken = default) =>
        client.CollectionExistsAsync(_options.NameOf(collection), cancellationToken);

    public async Task DropCollectionAsync(VectorCollection collection, CancellationToken cancellationToken = default)
    {
        var name = _options.NameOf(collection);
        if (await client.CollectionExistsAsync(name, cancellationToken))
        {
            await client.DeleteCollectionAsync(name, cancellationToken: cancellationToken);
            logger.LogWarning("Dropped Qdrant collection {Collection}", name);
        }

        _verified.TryRemove(name, out _);
    }

    public async Task UpsertAsync(VectorCollection collection, IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default)
    {
        if (points.Count == 0)
        {
            return;
        }

        var structs = points.Select(ToPointStruct).ToList();
        await client.UpsertAsync(_options.NameOf(collection), structs, cancellationToken: cancellationToken);
    }

    public async Task DeleteDocumentAsync(VectorCollection collection, string documentId, IReadOnlyCollection<long>? parentIds = null, CancellationToken cancellationToken = default)
    {
        await client.DeleteAsync(_options.NameOf(collection), BuildDocumentFilter(documentId, parentIds), cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorCollection collection, VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        Filter? filter = null;
        if (request.TextFilter is { } textFilter)
        {
            // Match or no value at all: untagged points must not disappear from filtered searches.
            filter = new Filter();
            filter.Should.Add(MatchText(textFilter.Field, textFilter.Text));
            filter.Should.Add(MatchKeyword(textFilter.Field, string.Empty)); // written as "" by the ingestion
            filter.Should.Add(IsEmpty(textFilter.Field));
            filter.Should.Add(IsNull(textFilter.Field));
        }

        var points = await client.QueryAsync(
            _options.NameOf(collection),
            request.Vector,
            filter: filter,
            scoreThreshold: request.ScoreThreshold,
            limit: (ulong)request.Limit,
            cancellationToken: cancellationToken);

        return points.Select(ToHit).ToList();
    }

    public void Dispose() => _lock.Dispose();

    internal static Filter BuildDocumentFilter(string documentId, IReadOnlyCollection<long>? parentIds)
    {
        var filter = new Filter();
        filter.Should.Add(MatchKeyword(VectorPayloadKeys.DocumentId, documentId));
        foreach (var parentId in parentIds ?? [])
        {
            filter.Should.Add(Match(VectorPayloadKeys.ParentId, parentId));
        }
        return filter;
    }

    internal static PointStruct ToPointStruct(VectorPoint point)
    {
        var result = new PointStruct
        {
            Id = new PointId { Num = (ulong)point.Id },
            Vectors = point.Vector
        };
        foreach (var (key, value) in point.Payload)
        {
            result.Payload[key] = value switch
            {
                string s => s,
                long l => l,
                int i => (long)i,
                double d => d,
                bool b => b,
                _ => throw new ArgumentException($"Unsupported payload type {value?.GetType().Name ?? "null"} for '{key}'.", nameof(point))
            };
        }
        return result;
    }

    internal static VectorSearchHit ToHit(ScoredPoint point) =>
        new((long)point.Id.Num, point.Score, point.Payload.ToDictionary(p => p.Key, p => FromValue(p.Value)));

    private static object? FromValue(Value value) => value.KindCase switch
    {
        Value.KindOneofCase.StringValue => value.StringValue,
        Value.KindOneofCase.IntegerValue => value.IntegerValue,
        Value.KindOneofCase.DoubleValue => value.DoubleValue,
        Value.KindOneofCase.BoolValue => value.BoolValue,
        _ => null
    };

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
