using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AsistenteAyuntamiento.Infrastructure.Messaging;

/// <summary>Thrown by a handler when retrying cannot help (the message goes straight to the DLQ).</summary>
public sealed class NonRetryableMessageException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>What happens to a message whose handler failed.</summary>
public enum FailureAction
{
    /// <summary>Republish to <c>Q.retry</c> with <c>x-retry-count + 1</c>.</summary>
    Retry,

    /// <summary>Nack without requeue: the broker routes it to <c>Q.dlq</c>.</summary>
    DeadLetter,

    /// <summary>Ack and drop (ephemeral messages such as notifications).</summary>
    Drop,
}

/// <summary>
/// Base class for RabbitMQ consumers: connects with backoff and never gives up while the host runs,
/// reconnects when the connection drops, deserializes, and applies the retry/dead-letter policy.
/// </summary>
public abstract class RabbitMqConsumerBase<TMessage>(
    IConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    RabbitMqConsumerHealth health,
    ILogger logger) : BackgroundService where TMessage : class
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _publishLock = new(1, 1);

    /// <summary>Name used for logs and health reporting.</summary>
    protected abstract string ConsumerName { get; }

    /// <summary>Declares the topology and returns the queue to consume from.</summary>
    protected abstract Task<string> DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken);

    /// <summary>Processes one message inside its own DI scope.</summary>
    protected abstract Task HandleAsync(TMessage message, IServiceProvider services, CancellationToken cancellationToken);

    protected virtual ushort Prefetch => 5;

    protected virtual int MaxRetries => RabbitMqTopology.DefaultMaxRetries;

    /// <summary>Whether failed messages are retried and dead-lettered (work queues) or dropped (notifications).</summary>
    protected virtual bool UsesRetryAndDeadLetter => true;

    /// <summary>Called once a message is dead-lettered (e.g. to mark the job as Failed).</summary>
    protected virtual Task OnDeadLetteredAsync(TMessage? message, Exception exception, IServiceProvider services, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>Retry policy, exposed for unit tests.</summary>
    public static FailureAction Decide(Exception exception, int retryCount, int maxRetries, bool usesRetryAndDeadLetter)
    {
        if (!usesRetryAndDeadLetter) return FailureAction.Drop;
        if (exception is JsonException or NonRetryableMessageException) return FailureAction.DeadLetter;
        return retryCount < maxRetries ? FailureAction.Retry : FailureAction.DeadLetter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await connectionFactory.CreateConnectionAsync(ConsumerName, stoppingToken);
                await using var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true,
                        consumerDispatchConcurrency: Prefetch),
                    stoppingToken);

                var queue = await DeclareTopologyAsync(channel, stoppingToken);
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: Prefetch, global: false, cancellationToken: stoppingToken);

                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                connection.ConnectionShutdownAsync += (_, args) =>
                {
                    logger.LogWarning("{Consumer}: RabbitMQ connection closed ({Reason})", ConsumerName, args.ReplyText);
                    closed.TrySetResult();
                    return Task.CompletedTask;
                };

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, queue, delivery, stoppingToken);
                await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);

                attempt = 0;
                health.Report(ConsumerName, connected: true);
                logger.LogInformation("{Consumer}: consuming from {Queue}", ConsumerName, queue);

                await closed.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                attempt++;
                logger.LogError(ex, "{Consumer}: cannot consume from RabbitMQ (attempt {Attempt})", ConsumerName, attempt);
            }

            health.Report(ConsumerName, connected: false);
            var delay = TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(attempt, 6))));
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        health.Report(ConsumerName, connected: false);
    }

    private async Task OnReceivedAsync(IChannel channel, string queue, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        TMessage? message = null;
        try
        {
            message = JsonSerializer.Deserialize<TMessage>(delivery.Body.Span, JsonOptions)
                ?? throw new JsonException("Empty message body.");

            using var scope = scopeFactory.CreateScope();
            await HandleAsync(message, scope.ServiceProvider, stoppingToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: leave the message unacked so the broker redelivers it.
        }
        catch (Exception ex)
        {
            var retryCount = RabbitMqTopology.GetRetryCount(delivery.BasicProperties);
            var action = Decide(ex, retryCount, MaxRetries, UsesRetryAndDeadLetter);

            try
            {
                switch (action)
                {
                    case FailureAction.Retry:
                        logger.LogWarning(ex, "{Consumer}: message failed (retry {Retry}/{Max}); scheduling a retry", ConsumerName, retryCount + 1, MaxRetries);
                        await PublishToRetryQueueAsync(channel, queue, delivery, retryCount + 1, stoppingToken);
                        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                        break;

                    case FailureAction.DeadLetter:
                        logger.LogError(ex, "{Consumer}: message sent to {DeadLetterQueue} after {Retries} retries", ConsumerName, RabbitMqTopology.DeadLetterQueue(queue), retryCount);
                        await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                        using (var scope = scopeFactory.CreateScope())
                        {
                            await OnDeadLetteredAsync(message, ex, scope.ServiceProvider, stoppingToken);
                        }
                        break;

                    default:
                        logger.LogError(ex, "{Consumer}: message dropped", ConsumerName);
                        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                        break;
                }
            }
            catch (Exception handlingEx)
            {
                // The message stays unacked and is redelivered when the channel is recovered.
                logger.LogError(handlingEx, "{Consumer}: could not apply failure action {Action}", ConsumerName, action);
            }
        }
    }

    private async Task PublishToRetryQueueAsync(IChannel channel, string queue, BasicDeliverEventArgs delivery, int retryCount, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = delivery.BasicProperties.ContentType ?? "application/json",
            MessageId = delivery.BasicProperties.MessageId,
            Headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>())
            {
                [RabbitMqTopology.RetryCountHeader] = retryCount,
            },
        };

        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            // Publisher confirms are enabled on this channel: this returns once the broker stored the message.
            await channel.BasicPublishAsync(string.Empty, RabbitMqTopology.RetryQueue(queue), mandatory: true, properties, delivery.Body, cancellationToken);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public override void Dispose()
    {
        _publishLock.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
