using System.Threading;
using System.Threading.Tasks;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public interface IDocumentIngestionService
{
    Task ProcessBlobAsync(string blobPath, string source, CancellationToken cancellationToken = default);
}
