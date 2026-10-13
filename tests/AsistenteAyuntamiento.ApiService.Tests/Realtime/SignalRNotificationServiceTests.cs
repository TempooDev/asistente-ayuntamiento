using AsistenteAyuntamiento.ApiService.Features.Notifications;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace AsistenteAyuntamiento.ApiService.Tests.Realtime;

/// <summary>Status batches reach the administrators as a few DocumentStatusesChanged messages.</summary>
public class SignalRNotificationServiceTests
{
    [Fact]
    public async Task Sends_one_message_per_batch_to_the_admin_group()
    {
        var sent = new List<(string Method, object?[] Args)>();
        var admins = new Mock<IClientProxy>();
        admins.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => sent.Add((method, args)))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(NotificationGroups.Admins)).Returns(admins.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var changes = Enumerable.Range(1, SignalRNotificationService.BatchSize * 2 + 1)
            .Select(i => new DocumentStatusChange($"BOE-A-{i}", "Queued"))
            .ToList();

        await new SignalRNotificationService(hub.Object).NotifyDocumentStatusesChangedAsync(changes);

        Assert.Equal(3, sent.Count);
        Assert.All(sent, s => Assert.Equal("DocumentStatusesChanged", s.Method));
        var payloads = sent.Select(s => Assert.IsType<DocumentStatusNotification[]>(Assert.Single(s.Args))).ToList();
        Assert.Equal([SignalRNotificationService.BatchSize, SignalRNotificationService.BatchSize, 1], payloads.Select(p => p.Length));
        Assert.Equal(changes.Select(c => c.DocumentId), payloads.SelectMany(p => p).Select(p => p.DocumentId));
        clients.Verify(c => c.All, Times.Never);
    }
}
