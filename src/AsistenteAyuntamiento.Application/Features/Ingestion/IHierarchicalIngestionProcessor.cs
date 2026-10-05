namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public interface IHierarchicalIngestionProcessor
{
    Task ProcessDocumentAsync(string blobPath, string documentId, CancellationToken cancellationToken);
}

