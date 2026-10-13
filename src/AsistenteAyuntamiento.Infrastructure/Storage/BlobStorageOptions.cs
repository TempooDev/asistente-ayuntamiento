namespace AsistenteAyuntamiento.Infrastructure.Storage;

/// <summary>Blob storage settings (configuration section <c>Blob</c>).</summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "Blob";

    public string BucketName { get; set; } = AsistenteAyuntamiento.Shared.AppConstants.BlobStorage.DefaultBucketName;
}
