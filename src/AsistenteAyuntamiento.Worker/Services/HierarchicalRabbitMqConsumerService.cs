using System.Text;
using System.Text.Json;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AsistenteAyuntamiento.Worker.Services;

public class HierarchicalRabbitMqConsumerService(IServiceProvider serviceProvider, ILogger<HierarchicalRabbitMqConsumerService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<HierarchicalRabbitMqConsumerService> _logger = logger;
    private readonly string _queueName = "documents_to_process_hierarchical";
    private IConnection? _connection;
    private IChannel? _channel;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Iniciando HierarchicalRabbitMqConsumerService");

        var connectionFactory = _serviceProvider.GetService<IConnectionFactory>();
        if (connectionFactory != null)
        {
            if (connectionFactory is ConnectionFactory cf)
            {
                cf.ConsumerDispatchConcurrency = 5;
            }

            var connected = false;
            var retryCount = 0;
            while (!connected && retryCount < 10 && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
                    _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                    await _channel.QueueDeclareAsync(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
                    await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 5, global: false, cancellationToken: cancellationToken);

                    connected = true;
                    _logger.LogInformation("Conectado a RabbitMQ (Hierarchical) exitosamente.");
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogError(ex, $"Error conectando a RabbitMQ (Intento {retryCount}/10). Reintentando en 5 segundos...");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        else
        {
            _logger.LogWarning("IConnectionFactory no está registrado.");
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
            _logger.LogInformation($"Mensaje jerárquico recibido de RabbitMQ: {message}");

            try
            {
                var docMsg = JsonSerializer.Deserialize<DocumentMessage>(message, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (docMsg != null && !string.IsNullOrEmpty(docMsg.BlobPath))
                {
                    using var scope = _serviceProvider.CreateScope();
                    var sourceKey = docMsg.Source.ToUpperInvariant();
                    var processor = scope.ServiceProvider.GetKeyedService<IHierarchicalIngestionProcessor>(sourceKey);

                    if (processor != null)
                    {
                        await processor.ProcessDocumentAsync(docMsg.BlobPath, docMsg.DocumentId, stoppingToken);
                    }
                    else
                    {
                        _logger.LogWarning("No processor found for source {Source}", docMsg.Source);
                    }
                }

                await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico procesando el documento jerárquico.");
                await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(queue: _queueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel != null) await _channel.CloseAsync(cancellationToken: cancellationToken);
        if (_connection != null) await _connection.CloseAsync(cancellationToken: cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
