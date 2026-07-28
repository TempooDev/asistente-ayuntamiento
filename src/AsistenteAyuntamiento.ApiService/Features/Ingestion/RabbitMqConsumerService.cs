using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;

namespace AsistenteAyuntamiento.ApiService.Features.Ingestion;

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
            try
            {
                _connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await _channel.QueueDeclareAsync(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
                await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error conectando a RabbitMQ al iniciar el servicio");
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
                _logger.LogError(ex, "Error procesando el documento. Devolviendo a la cola (NACK).");
                // Re-encolar si falla
                await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
                await Task.Delay(5000, stoppingToken); // Simple backoff
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

public class DocumentMessage
{
    [System.Text.Json.Serialization.JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("blob_path")]
    public string BlobPath { get; set; } = string.Empty;
}

public class ScrapedDocument
{
    [System.Text.Json.Serialization.JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("text")]
    public string Content { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    public DocumentMetadata? Metadata { get; set; }
}

public class DocumentMetadata
{
    [System.Text.Json.Serialization.JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("titulo")]
    public string Title { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("departamento")]
    public string Department { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("fecha_publicacion")]
    public string PublicationDate { get; set; } = string.Empty;
}
