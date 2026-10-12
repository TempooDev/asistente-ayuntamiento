using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

public class SignalRNotificationService(IHubContext<NotificationHub> hubContext) : INotificationService
{
    private readonly IHubContext<NotificationHub> _hubContext = hubContext;

    /// <summary>
    /// Document ingestion works on the shared knowledge base, so only administrators (who manage it) are notified.
    /// </summary>
    public async Task NotifyDocumentStatusChangedAsync(string documentId, string newStatus)
    {
        await _hubContext.Clients.Group(NotificationGroups.Admins)
            .SendAsync("DocumentStatusChanged", new DocumentStatusNotification(documentId, newStatus));
    }
}
