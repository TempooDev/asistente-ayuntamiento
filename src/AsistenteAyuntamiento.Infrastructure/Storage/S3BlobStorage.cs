using System.Net;
using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;
using AsistenteAyuntamiento.Application.Common.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsistenteAyuntamiento.Infrastructure.Storage;

/// <summary>S3/MinIO implementation of <see cref="IBlobStorage"/>.</summary>
public sealed class S3BlobStorage(IAmazonS3 s3, IOptions<BlobStorageOptions> options, ILogger<S3BlobStorage> logger) : IBlobStorage
{
    private readonly string _bucket = options.Value.BucketName;

    public async Task EnsureContainerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucket, UseClientRegion = true }, cancellationToken);
            logger.LogInformation("Created bucket {Bucket}", _bucket);
        }
        catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // Already there.
        }
        catch (AmazonS3Exception e) when (e.ErrorCode == "AccessDenied")
        {
            logger.LogWarning("Access denied creating bucket {Bucket}. Create it manually or grant s3:CreateBucket.", _bucket);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not create bucket {Bucket}. It might already exist or require manual creation.", _bucket);
        }
    }

    public async IAsyncEnumerable<BlobObjectInfo> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix };
        ListObjectsV2Response? response;
        do
        {
            response = await s3.ListObjectsV2Async(request, cancellationToken);
            foreach (var item in response?.S3Objects ?? [])
            {
                if (item?.Key is { } key)
                {
                    yield return new BlobObjectInfo(key, item.Size, item.LastModified);
                }
            }
            request.ContinuationToken = response?.NextContinuationToken;
        } while (response?.IsTruncated == true);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await s3.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = key }, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey" || ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BlobNotFoundException(key, ex);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3.GetObjectMetadataAsync(_bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey" || ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
