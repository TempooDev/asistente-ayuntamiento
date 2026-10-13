namespace AsistenteAyuntamiento.Application.Common.Storage;

/// <summary>Metadata of a stored object.</summary>
public sealed record BlobObjectInfo(string Key, long? Size, DateTime? LastModified);
