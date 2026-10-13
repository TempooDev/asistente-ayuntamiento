namespace AsistenteAyuntamiento.Application.Common.Interfaces;

/// <summary>New status of one ingestion document.</summary>
public sealed record DocumentStatusChange(string DocumentId, string Status);

public interface INotificationService
{
    Task NotifyDocumentStatusChangedAsync(string documentId, string newStatus);

    /// <summary>
    /// Several changes at once (enqueuing hundreds of documents). Transports that can send a batch override this;
    /// the default sends them one by one.
    /// </summary>
    async Task NotifyDocumentStatusesChangedAsync(IReadOnlyList<DocumentStatusChange> changes)
    {
        foreach (var change in changes)
        {
            await NotifyDocumentStatusChangedAsync(change.DocumentId, change.Status);
        }
    }
}
