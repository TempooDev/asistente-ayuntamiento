namespace AsistenteAyuntamiento.Worker.Services;

public interface IHierarchicalIngestionProcessor
{
    Task ProcessDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken);
}
