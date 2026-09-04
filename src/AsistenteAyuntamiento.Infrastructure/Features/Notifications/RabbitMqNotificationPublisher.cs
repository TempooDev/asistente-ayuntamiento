using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Infrastructure.Common;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AsistenteAyuntamiento.Infrastructure.Features.Notifications;

public class RabbitMqNotificationPublisher(IConnectionFactory connectionFactory, ILogger<RabbitMqNotificationPublisher> logger) : INotificationService, IAsyncDisposable
{
    private readonly IConnectionFactory _connectionFactory = connectionFactory;
    private readonly ILogger<RabbitMqNotificationPublisher> _logger = logger;
    private IConnection? _connection;
    private IChannel? _channel;

    // Semaphores to ensure thread-safety for connection and channel usage
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _channelLock = new(1, 1);

    private async Task EnsureConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection != null && _connection.IsOpen && _channel != null && _channel.IsOpen)
            return;

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection == null || !_connection.IsOpen)
            {
                _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            }

            if (_channel == null || !_channel.IsOpen)
            {
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await _channel.ExchangeDeclareAsync(exchange: RabbitMqConstants.DocumentNotificationsExchange, type: ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task NotifyDocumentStatusChangedAsync(string documentId, string newStatus)
    {
        try
        {
            await EnsureConnectionAsync();

            var message = new { DocumentId = documentId, NewStatus = newStatus };
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            var props = new BasicProperties { Persistent = true };

            // Channel operations must be thread-safe in RabbitMQ
            await _channelLock.WaitAsync();
            try
            {
                if (_channel != null)
                {
                    await _channel.BasicPublishAsync(
                        exchange: RabbitMqConstants.DocumentNotificationsExchange,
                        routingKey: string.Empty,
                        mandatory: false,
                        basicProperties: props,
                        body: body,
                        cancellationToken: default);
                }
            }
            finally
            {
                _channelLock.Release();
            }

            _logger.LogInformation("Notificación publicada vía RabbitMQ para documento {DocumentId}: {Status}", documentId, newStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al publicar notificación de RabbitMQ para el documento {DocumentId}", documentId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel != null && _channel.IsOpen) await _channel.CloseAsync();
        if (_connection != null && _connection.IsOpen) await _connection.CloseAsync();

        _connectionLock.Dispose();
        _channelLock.Dispose();
    }
}
