using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using AsistenteAyuntamiento.Application.Features.Ingestion;

namespace AsistenteAyuntamiento.Infrastructure.Features.Ingestion;

public class RabbitMqConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private readonly string _queueName = "documents_to_process";
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConsumerService(IServiceProvider serviceProvider, ILogger<RabbitMqConsumerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Iniciando RabbitMqConsumerService");
        
        // Aspire registers IConnection via DI when Aspire.RabbitMQ.Client is used
        var connectionFactory = _serviceProvider.GetService<IConnectionFactory>();
        if (connectionFactory != null)
        {
            if (connectionFactory is ConnectionFactory cf)
            {
                // Habilitamos despacho concurrente en el consumidor para procesar en paralelo
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
                    
                    // Aumentamos prefetchCount a 5 para procesar múltiples documentos en paralelo
                    await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 5, global: false, cancellationToken: cancellationToken);
                    
                    connected = true;
                    _logger.LogInformation("Conectado a RabbitMQ exitosamente.");
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
            _logger.LogWarning("IConnectionFactory no está registrado. Asegúrate de tener builder.AddRabbitMQClient() en Program.cs");
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
            _logger.LogInformation($"Mensaje recibido de RabbitMQ: {message}");

            try
            {
                var docMsg = JsonSerializer.Deserialize<DocumentMessage>(message, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (docMsg != null && !string.IsNullOrEmpty(docMsg.BlobPath))
                {
                    await ProcessDocumentAsync(docMsg, stoppingToken);
                }
                
                // Procesado exitosamente o parseo fallido de forma irrecuperable
                await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico procesando el documento. Descartando mensaje (NACK sin requeue) para evitar bucles infinitos.");
                // NACK sin requeue para que se envíe a DLQ o se descarte, evitando bloquear la cola
                await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(queue: _queueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
    }

    private async Task ProcessDocumentAsync(DocumentMessage docMsg, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<DocumentIngestionService>();
        await ingestionService.ProcessBlobAsync(docMsg.BlobPath, docMsg.Source, cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel != null) await _channel.CloseAsync(cancellationToken: cancellationToken);
        if (_connection != null) await _connection.CloseAsync(cancellationToken: cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
