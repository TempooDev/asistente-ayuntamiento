using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

public class SignalRNotificationService(IHubContext<NotificationHub> hubContext) : INotificationService
{
    private readonly IHubContext<NotificationHub> _hubContext = hubContext;

    public async Task NotifyDocumentStatusChangedAsync(string documentId, string newStatus)
    {
        await _hubContext.Clients.All.SendAsync("DocumentStatusChanged", documentId, newStatus);
    }
}
