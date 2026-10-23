using AsistenteAyuntamiento.Domain.Common.Enums;
using RabbitMQ.Client;

namespace AsistenteAyuntamiento.Infrastructure.Messaging;

/// <summary>
/// Single definition of the RabbitMQ work queues (see docs/messaging-contract.md; the Go scraper mirrors it).
/// For each work queue Q:
/// <list type="bullet">
/// <item><c>Q</c>: dead-letters to <c>Q.dlq</c> (via the default exchange).</item>
/// <item><c>Q.retry</c>: holds failed messages for <see cref="RetryDelayMs"/>, then dead-letters back to <c>Q</c>.</item>
/// <item><c>Q.dlq</c>: messages that exhausted their retries or could not be parsed.</item>
/// </list>
/// Queue arguments cannot change on an existing queue: deploying this topology requires recreating the queues.
/// </summary>
public static class RabbitMqTopology
{
    public const string BaselineQueue = "documents_to_process_baseline";
    public const string HierarchicalQueue = "documents_to_process_hierarchical";

    /// <summary>Chunking study variants; only the administrator enqueues to them (the scraper does not).</summary>
    public const string RecursiveQueue = "documents_to_process_recursive";
    public const string SemanticQueue = "documents_to_process_semantic";
    public const string DocumentNotificationsExchange = "document_notifications_exchange";

    /// <summary>Header carrying how many times a message has been retried.</summary>
    public const string RetryCountHeader = "x-retry-count";

    /// <summary>Delay before a failed message is redelivered. Must match the Go scraper (int32 on the wire).</summary>
    public const int RetryDelayMs = 30_000;

    public const int DefaultMaxRetries = 3;

    /// <summary>Work queue of each ingestion pipeline.</summary>
    public static string QueueOf(PipelineType pipeline) => pipeline switch
    {
        PipelineType.Baseline => BaselineQueue,
        PipelineType.Hierarchical => HierarchicalQueue,
        PipelineType.Recursive => RecursiveQueue,
        PipelineType.Semantic => SemanticQueue,
        _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, null),
    };

    public static string RetryQueue(string queue) => $"{queue}.retry";

    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";

    /// <summary>Declares <c>Q</c>, <c>Q.retry</c> and <c>Q.dlq</c> with the shared arguments.</summary>
    public static async Task DeclareWorkQueueAsync(IChannel channel, string queue, CancellationToken cancellationToken = default, int retryDelayMs = RetryDelayMs)
    {
        await channel.QueueDeclareAsync(DeadLetterQueue(queue), durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(RetryQueue(queue), durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = retryDelayMs,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = queue,
            },
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = DeadLetterQueue(queue),
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>Reads <see cref="RetryCountHeader"/> regardless of the integer type the publisher used.</summary>
    public static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(RetryCountHeader, out var value))
        {
            return 0;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte b => b,
            short s => s,
            byte[] bytes when int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0,
        };
    }
}
