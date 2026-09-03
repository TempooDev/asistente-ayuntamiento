namespace AsistenteAyuntamiento.Application.Common.Interfaces;

public interface INotificationService
{
    Task NotifyDocumentStatusChangedAsync(string documentId, string newStatus);
}
