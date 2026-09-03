using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;

namespace AsistenteAyuntamiento.Infrastructure.Features.Notifications;

public class RabbitMqNotificationPublisher : INotificationService, IAsyncDisposable
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqNotificationPublisher> _logger;
    private const string ExchangeName = "document_notifications_exchange";
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqNotificationPublisher(IConnectionFactory connectionFactory, ILogger<RabbitMqNotificationPublisher> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    private async Task EnsureConnectionAsync()
    {
        if (_connection == null || !_connection.IsOpen)
        {
            _connection = await _connectionFactory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();
            await _channel.ExchangeDeclareAsync(exchange: ExchangeName, type: ExchangeType.Fanout, durable: true);
        }
    }

    public async Task NotifyDocumentStatusChangedAsync(string documentId, string newStatus)
    {
        try
        {
            await EnsureConnectionAsync();
            if (_channel != null)
            {
                var message = new { DocumentId = documentId, NewStatus = newStatus };
                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
                
                var props = new BasicProperties { Persistent = true };
                
                await _channel.BasicPublishAsync(
                    exchange: ExchangeName,
                    routingKey: string.Empty,
                    mandatory: false,
                    basicProperties: props,
                    body: body,
                    cancellationToken: default);
                    
                _logger.LogInformation("Notificación publicada vía RabbitMQ para documento {DocumentId}: {Status}", documentId, newStatus);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al publicar notificación de RabbitMQ para el documento {DocumentId}", documentId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel != null) await _channel.CloseAsync();
        if (_connection != null) await _connection.CloseAsync();
    }
}
