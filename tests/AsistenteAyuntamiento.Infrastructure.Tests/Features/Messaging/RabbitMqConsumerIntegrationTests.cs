using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;

/// <summary>
/// Retry, dead-lettering and persistence against a real broker (spec: messaging-reliability).
/// Requires Docker: skipped automatically where it is not available (it runs in CI).
/// </summary>
public class RabbitMqConsumerIntegrationTests : IAsyncLifetime
{
    private RabbitMqContainer? _broker;
    private ConnectionFactory _factory = null!;

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable) return;
        _broker = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();
        await _broker.StartAsync();
        _factory = new ConnectionFactory { Uri = new Uri(_broker.GetConnectionString()) };
    }

    public async Task DisposeAsync()
    {
        if (_broker is not null) await _broker.DisposeAsync();
    }

    [DockerFact]
    public async Task Failing_message_is_retried_then_dead_lettered_while_good_messages_are_processed()
    {
        var queue = $"test_{Guid.NewGuid():N}";
        var consumer = new TestConsumer(_factory, queue, failingDocumentId: "poison");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await consumer.StartAsync(cts.Token);

        await consumer.TopologyDeclared.Task.WaitAsync(cts.Token);
        await using var connection = await _factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await Publish(channel, queue, "ok-1");
        await Publish(channel, queue, "poison");

        // 1 attempt + MaxRetries (1) retries, then the DLQ.
        await WaitUntilAsync(async () => await channel.MessageCountAsync(RabbitMqTopology.DeadLetterQueue(queue)) == 1, cts.Token);

        consumer.Handled.Should().Contain("ok-1");
        consumer.Attempts["poison"].Should().Be(2);
        (await channel.MessageCountAsync(queue)).Should().Be(0);

        await consumer.StopAsync(CancellationToken.None);
    }

    [DockerFact]
    public async Task Published_ingestion_messages_are_persistent()
    {
        var publisher = new RabbitMqIngestionPublisher(_factory, NullLogger<RabbitMqIngestionPublisher>.Instance);
        await publisher.PublishAsync(
            new DocumentMessage { DocumentId = "BOE-A-1", Source = "BOE", BlobPath = "json/BOE/BOE-A-1.json" },
            Application.Common.Messaging.IngestionPipelines.Baseline);

        await using var connection = await _factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var message = await channel.BasicGetAsync(RabbitMqTopology.BaselineQueue, autoAck: true);

        message.Should().NotBeNull();
        message!.BasicProperties.Persistent.Should().BeTrue();
        await publisher.DisposeAsync();
    }

    [DockerFact]
    public async Task Confirmed_ingestion_messages_survive_a_broker_restart()
    {
        var publisher = new RabbitMqIngestionPublisher(_factory, NullLogger<RabbitMqIngestionPublisher>.Instance);
        await publisher.PublishAsync(
            new DocumentMessage { DocumentId = "BOE-A-restart", Source = "BOE", BlobPath = "json/BOE/BOE-A-restart.json" },
            Application.Common.Messaging.IngestionPipelines.Baseline | Application.Common.Messaging.IngestionPipelines.Hierarchical);
        await publisher.DisposeAsync();

        // Same container, so the same data directory; the host port may change.
        await _broker!.StopAsync();
        await _broker.StartAsync();
        var factory = new ConnectionFactory { Uri = new Uri(_broker.GetConnectionString()) };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        foreach (var queue in new[] { RabbitMqTopology.BaselineQueue, RabbitMqTopology.HierarchicalQueue })
        {
            var message = await channel.BasicGetAsync(queue, autoAck: true);
            message.Should().NotBeNull($"the message in {queue} is persistent and was confirmed before the restart");
            JsonSerializer.Deserialize<DocumentMessage>(message!.Body.Span, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
                .DocumentId.Should().Be("BOE-A-restart");
        }
    }

    [DockerFact]
    public async Task Consumer_continues_the_trace_of_a_dotnet_publisher()
    {
        using var listener = ListenToAllSources();
        var queue = $"test_{Guid.NewGuid():N}";
        var consumer = new TestConsumer(_factory, queue, failingDocumentId: "none");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await consumer.StartAsync(cts.Token);
        await consumer.TopologyDeclared.Task.WaitAsync(cts.Token);

        await using var connection = await _factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        using var source = new ActivitySource("test.producer");
        ActivityTraceId producerTrace;
        using (var producer = source.StartActivity("enqueue", ActivityKind.Producer)!)
        {
            producerTrace = producer.TraceId;
            await channel.BasicPublishAsync(string.Empty, queue, mandatory: false,
                new BasicProperties { Persistent = true, Headers = RabbitMqTraceContext.Inject() },
                JsonSerializer.SerializeToUtf8Bytes(new DocumentMessage { DocumentId = "traced", Source = "BOE", BlobPath = "x" }));
        }

        await WaitUntilAsync(() => Task.FromResult(consumer.TraceIds.ContainsKey("traced")), cts.Token);
        consumer.TraceIds["traced"].Should().Be(producerTrace.ToHexString());
        await consumer.StopAsync(CancellationToken.None);
    }

    [DockerFact]
    public async Task Consumer_continues_a_traceparent_written_by_another_service()
    {
        var queue = $"test_{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Published before any .NET listener exists: with one, RabbitMQ.Client's own publish span would replace the
        // header. The Go scraper writes it as a string (RabbitMQ delivers it as bytes).
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        await using (var connection = await _factory.CreateConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
        {
            await RabbitMqTopology.DeclareWorkQueueAsync(channel, queue, cts.Token, retryDelayMs: 300);
            await channel.BasicPublishAsync(string.Empty, queue, mandatory: false,
                new BasicProperties
                {
                    Persistent = true,
                    Headers = new Dictionary<string, object?> { ["traceparent"] = $"00-{traceId}-00f067aa0ba902b7-01" },
                },
                JsonSerializer.SerializeToUtf8Bytes(new DocumentMessage { DocumentId = "from-go", Source = "BOE", BlobPath = "x" }));
        }

        using var listener = ListenToAllSources();
        var consumer = new TestConsumer(_factory, queue, failingDocumentId: "none");
        await consumer.StartAsync(cts.Token);

        await WaitUntilAsync(() => Task.FromResult(consumer.TraceIds.ContainsKey("from-go")), cts.Token);
        consumer.TraceIds["from-go"].Should().Be(traceId);
        await consumer.StopAsync(CancellationToken.None);
    }

    private static ActivityListener ListenToAllSources()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static async Task Publish(IChannel channel, string queue, string documentId) =>
        await channel.BasicPublishAsync(string.Empty, queue, mandatory: false,
            new BasicProperties { Persistent = true },
            JsonSerializer.SerializeToUtf8Bytes(new DocumentMessage { DocumentId = documentId, Source = "BOE", BlobPath = "x" }));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        while (!await condition())
        {
            await Task.Delay(200, cancellationToken);
        }
    }

    private sealed class TestConsumer(IConnectionFactory factory, string queue, string failingDocumentId)
        : RabbitMqConsumerBase<DocumentMessage>(factory, new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new RabbitMqConsumerHealth(), NullLogger.Instance)
    {
        public ConcurrentBag<string> Handled { get; } = [];
        public ConcurrentDictionary<string, int> Attempts { get; } = new();
        public ConcurrentDictionary<string, string> TraceIds { get; } = new();
        public TaskCompletionSource TopologyDeclared { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override string ConsumerName => "test";
        protected override int MaxRetries => 1;

        protected override async Task<string> DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
        {
            await RabbitMqTopology.DeclareWorkQueueAsync(channel, queue, cancellationToken, retryDelayMs: 300);
            TopologyDeclared.TrySetResult();
            return queue;
        }

        protected override Task HandleAsync(DocumentMessage message, IServiceProvider services, CancellationToken cancellationToken)
        {
            Attempts.AddOrUpdate(message.DocumentId, 1, (_, n) => n + 1);
            if (Activity.Current is { } activity) TraceIds[message.DocumentId] = activity.TraceId.ToHexString();
            if (message.DocumentId == failingDocumentId) throw new TimeoutException("transient");
            Handled.Add(message.DocumentId);
            return Task.CompletedTask;
        }
    }
}

/// <summary>A fact that is skipped when no Docker daemon is reachable.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public static bool IsDockerAvailable { get; } =
        File.Exists("/var/run/docker.sock")
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST"))
        || (OperatingSystem.IsWindows() && File.Exists(@"\\.\pipe\docker_engine"));

    public DockerFactAttribute()
    {
        if (!IsDockerAvailable)
        {
            Skip = "Docker is not available.";
        }
    }
}
