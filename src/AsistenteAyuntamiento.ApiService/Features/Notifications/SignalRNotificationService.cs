using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

/// <summary>
/// Document ingestion works on the shared knowledge base, so only administrators (who manage it) are notified.
/// </summary>
public class SignalRNotificationService(IHubContext<NotificationHub> hubContext) : INotificationService
{
    /// <summary>Changes per <c>DocumentStatusesChanged</c> message, to keep each message small.</summary>
    public const int BatchSize = 500;

    private readonly IHubContext<NotificationHub> _hubContext = hubContext;

    public async Task NotifyDocumentStatusChangedAsync(string documentId, string newStatus)
    {
        await _hubContext.Clients.Group(NotificationGroups.Admins)
            .SendAsync("DocumentStatusChanged", new DocumentStatusNotification(documentId, newStatus));
    }

    /// <summary>One <c>DocumentStatusesChanged</c> event per <see cref="BatchSize"/> changes instead of one per document.</summary>
    public async Task NotifyDocumentStatusesChangedAsync(IReadOnlyList<DocumentStatusChange> changes)
    {
        foreach (var batch in changes.Chunk(BatchSize))
        {
            await _hubContext.Clients.Group(NotificationGroups.Admins)
                .SendAsync("DocumentStatusesChanged", batch.Select(c => new DocumentStatusNotification(c.DocumentId, c.Status)).ToArray());
        }
    }
}
