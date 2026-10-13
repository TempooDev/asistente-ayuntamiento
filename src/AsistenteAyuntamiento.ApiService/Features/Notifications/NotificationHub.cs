using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

/// <summary>
/// Hub for system events. Each connection joins its tenant group and, for administrators,
/// the admin group, so events are delivered only to the users they concern.
/// </summary>
[Authorize]
public class NotificationHub(ICurrentTenantService tenantService) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = tenantService.TenantId;
        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.Tenant(tenantId));
        }

        if (NotificationGroups.IsPlatformAdmin(Context.User))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.PlatformAdmins);
        }

        await base.OnConnectedAsync();
    }
}
