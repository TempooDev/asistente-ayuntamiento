using System.Text.Json;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Messaging;
using FluentAssertions;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;

/// <summary>Retry / dead-letter decisions (spec: messaging-reliability).</summary>
public class RetryPolicyTests
{
    private static FailureAction Decide(Exception ex, int retryCount, bool workQueue = true) =>
        RabbitMqConsumerBase<DocumentMessage>.Decide(ex, retryCount, maxRetries: 3, usesRetryAndDeadLetter: workQueue);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Transient_failure_below_the_limit_is_retried(int retryCount) =>
        Decide(new TimeoutException(), retryCount).Should().Be(FailureAction.Retry);

    [Fact]
    public void Failure_after_the_last_retry_is_dead_lettered() =>
        Decide(new TimeoutException(), retryCount: 3).Should().Be(FailureAction.DeadLetter);

    [Fact]
    public void Malformed_message_goes_straight_to_the_dead_letter_queue() =>
        Decide(new JsonException("bad json"), retryCount: 0).Should().Be(FailureAction.DeadLetter);

    [Fact]
    public void Non_retryable_failure_goes_straight_to_the_dead_letter_queue() =>
        Decide(new NonRetryableMessageException("unknown source"), retryCount: 0).Should().Be(FailureAction.DeadLetter);

    [Fact]
    public void Ephemeral_consumers_drop_failed_messages() =>
        Decide(new TimeoutException(), retryCount: 0, workQueue: false).Should().Be(FailureAction.Drop);

    [Theory]
    [InlineData(null, 0)]
    [InlineData(2, 2)]
    [InlineData(5L, 5)]
    public void Retry_count_header_is_read_whatever_the_integer_type(object? header, int expected)
    {
        var properties = new Mock<IReadOnlyBasicProperties>();
        properties.Setup(p => p.Headers).Returns(header is null
            ? null
            : new Dictionary<string, object?> { [RabbitMqTopology.RetryCountHeader] = header });

        RabbitMqTopology.GetRetryCount(properties.Object).Should().Be(expected);
    }
}
