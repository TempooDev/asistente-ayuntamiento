using AsistenteAyuntamiento.Application.Common.Interfaces;

namespace AsistenteAyuntamiento.Application.Common.Notifications;

/// <summary>
/// No-op <see cref="INotificationService"/> for hosts without a notification transport,
/// so services can depend on the interface without null checks.
/// </summary>
public sealed class NullNotificationService : INotificationService
{
    public Task NotifyDocumentStatusChangedAsync(string documentId, string newStatus) => Task.CompletedTask;
}
