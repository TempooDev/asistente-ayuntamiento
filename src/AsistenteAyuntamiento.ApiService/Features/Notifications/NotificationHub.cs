namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

[Authorize]
public class NotificationHub : Hub
{
    // Hub specifically for system-wide events and notifications
}
