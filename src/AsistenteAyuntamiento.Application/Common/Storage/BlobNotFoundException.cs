namespace AsistenteAyuntamiento.Application.Common.Storage;

/// <summary>The requested object does not exist in blob storage.</summary>
public sealed class BlobNotFoundException(string key, Exception? innerException = null)
    : Exception($"El blob {key} no existe en el almacenamiento.", innerException)
{
    public string Key { get; } = key;
}
