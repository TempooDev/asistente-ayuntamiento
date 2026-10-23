using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Messaging;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AsistenteAyuntamiento.Infrastructure.Messaging;

/// <summary>
/// Publishes ingestion messages as persistent messages on a channel with publisher confirms:
/// <see cref="PublishAsync"/> returns only after the broker stored them, and throws otherwise.
/// One long-lived connection/channel, serialized with a lock and recreated when closed.
/// </summary>
public sealed class RabbitMqIngestionPublisher(IConnectionFactory connectionFactory, ILogger<RabbitMqIngestionPublisher> logger)
    : IIngestionQueuePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(DocumentMessage message, IngestionPipelines pipelines, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.DocumentId,
            Headers = RabbitMqTraceContext.Inject(),
        };

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);

            foreach (var pipeline in pipelines.ToPipelineTypes())
            {
                await channel.BasicPublishAsync(string.Empty, RabbitMqTopology.QueueOf(pipeline), mandatory: true, properties, body, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not publish document {DocumentId} to the ingestion queues", message.DocumentId);
            await ResetAsync();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await ResetAsync();
        _connection = await connectionFactory.CreateConnectionAsync("ingestion-publisher", cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        foreach (var pipeline in Enum.GetValues<PipelineType>())
        {
            await RabbitMqTopology.DeclareWorkQueueAsync(_channel, RabbitMqTopology.QueueOf(pipeline), cancellationToken);
        }
        return _channel;
    }

    private async Task ResetAsync()
    {
        try
        {
            if (_channel is not null) await _channel.DisposeAsync();
            if (_connection is not null) await _connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error disposing the ingestion publisher connection");
        }
        finally
        {
            _channel = null;
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _lock.Dispose();
    }
}
