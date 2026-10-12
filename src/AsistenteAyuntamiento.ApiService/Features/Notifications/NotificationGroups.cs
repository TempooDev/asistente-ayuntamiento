using System.Security.Claims;
using AsistenteAyuntamiento.ApiService.Security;

namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

/// <summary>
/// SignalR group names. Notifications are always sent to a group, never with <c>Clients.All</c>.
/// </summary>
public static class NotificationGroups
{
    /// <summary>Administrators of any tenant: ingestion and scraper events concern the shared (global) knowledge base.</summary>
    public const string Admins = "role:admin";

    public static string Tenant(string tenantId) => $"tenant:{tenantId}";

    public static bool IsAdmin(ClaimsPrincipal? user) => user is not null && AuthorizationExtensions.HasRole(user, Roles.Admin);
}

/// <summary>Payload of the <c>DocumentStatusChanged</c> event (one object, camelCase on the wire).</summary>
public sealed record DocumentStatusNotification(string DocumentId, string Status, string? ErrorMessage = null);

/// <summary>Payload of the <c>ScraperStateChanged</c> event.</summary>
public sealed record ScraperStateNotification(bool IsScraping, string Message);
