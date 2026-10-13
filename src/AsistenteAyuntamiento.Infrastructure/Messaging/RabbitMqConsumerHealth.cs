using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AsistenteAyuntamiento.Infrastructure.Messaging;

/// <summary>Connection state of every RabbitMQ consumer in the process, reported as a health check.</summary>
public sealed class RabbitMqConsumerHealth : IHealthCheck
{
    private readonly ConcurrentDictionary<string, bool> _consumers = new();

    public void Report(string consumer, bool connected) => _consumers[consumer] = connected;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var disconnected = _consumers.Where(c => !c.Value).Select(c => c.Key).ToList();

        return Task.FromResult(disconnected.Count == 0
            ? HealthCheckResult.Healthy($"{_consumers.Count} RabbitMQ consumer(s) connected")
            : HealthCheckResult.Unhealthy($"RabbitMQ consumer(s) not connected: {string.Join(", ", disconnected)}"));
    }
}
