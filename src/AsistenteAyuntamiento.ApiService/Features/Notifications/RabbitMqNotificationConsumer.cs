using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

public class RabbitMqNotificationConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqNotificationConsumer> _logger;
    private const string ExchangeName = "document_notifications_exchange";
    private readonly string _queueName = $"api_notifications_{Guid.NewGuid():N}"; // Cola temporal para cada instancia de la API
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqNotificationConsumer(IServiceProvider serviceProvider, ILogger<RabbitMqNotificationConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Iniciando RabbitMqNotificationConsumer");
        
        var connectionFactory = _serviceProvider.GetService<IConnectionFactory>();
        if (connectionFactory != null)
        {
            var connected = false;
            var retryCount = 0;
            while (!connected && retryCount < 10 && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
                    _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                    
                    await _channel.ExchangeDeclareAsync(exchange: ExchangeName, type: ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);
                    
                    // Cola exclusiva para recibir los eventos
                    await _channel.QueueDeclareAsync(queue: _queueName, durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken);
                    await _channel.QueueBindAsync(queue: _queueName, exchange: ExchangeName, routingKey: string.Empty, cancellationToken: cancellationToken);
                    
                    connected = true;
                    _logger.LogInformation("Notification Consumer conectado a RabbitMQ exitosamente.");
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogError(ex, $"Error conectando a RabbitMQ para notificaciones (Intento {retryCount}/10). Reintentando...");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        else
        {
            _logger.LogWarning("IConnectionFactory no está registrado para notificaciones.");
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_channel == null) return;

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            try
            {
                var docEvent = JsonSerializer.Deserialize<DocumentNotificationEvent>(message, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (docEvent != null && !string.IsNullOrEmpty(docEvent.DocumentId))
                {
                    using var scope = _serviceProvider.CreateScope();
                    var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                    await notificationService.NotifyDocumentStatusChangedAsync(docEvent.DocumentId, docEvent.NewStatus ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando notificación entrante.");
            }
            
            await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };

        await _channel.BasicConsumeAsync(queue: _queueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel != null) await _channel.CloseAsync(cancellationToken: cancellationToken);
        if (_connection != null) await _connection.CloseAsync(cancellationToken: cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private class DocumentNotificationEvent
    {
        public string? DocumentId { get; set; }
        public string? NewStatus { get; set; }
    }
}
