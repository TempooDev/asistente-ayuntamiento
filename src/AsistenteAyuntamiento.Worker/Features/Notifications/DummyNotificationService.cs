using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Worker.Features.Notifications;

public class DummyNotificationService : INotificationService
{
    private readonly ILogger<DummyNotificationService> _logger;

    public DummyNotificationService(ILogger<DummyNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyDocumentStatusChangedAsync(string documentId, string newStatus)
    {
        _logger.LogInformation("Document {DocumentId} status changed to {NewStatus}", documentId, newStatus);
        return Task.CompletedTask;
    }
}
