using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

/// <summary>
/// Relays document status events published by the Worker to the SignalR clients of this API instance.
/// Each instance binds its own exclusive, auto-delete queue to the fanout exchange; events are ephemeral,
/// so failures are logged and dropped (no retry / DLQ).
/// </summary>
public sealed class RabbitMqNotificationConsumer(
    IConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    RabbitMqConsumerHealth health,
    ILogger<RabbitMqNotificationConsumer> logger)
    : RabbitMqConsumerBase<RabbitMqNotificationConsumer.DocumentNotificationEvent>(connectionFactory, scopeFactory, health, logger)
{
    private readonly string _queueName = $"api_notifications_{Guid.NewGuid():N}";

    protected override string ConsumerName => "api-notifications";

    protected override bool UsesRetryAndDeadLetter => false;

    protected override async Task<string> DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(RabbitMqTopology.DocumentNotificationsExchange, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(_queueName, durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(_queueName, RabbitMqTopology.DocumentNotificationsExchange, routingKey: string.Empty, cancellationToken: cancellationToken);
        return _queueName;
    }

    protected override Task HandleAsync(DocumentNotificationEvent message, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message.DocumentId))
        {
            return Task.CompletedTask;
        }

        return services.GetRequiredService<INotificationService>()
            .NotifyDocumentStatusChangedAsync(message.DocumentId, message.NewStatus ?? string.Empty);
    }

    public sealed class DocumentNotificationEvent
    {
        public string? DocumentId { get; set; }
        public string? NewStatus { get; set; }
    }
}
