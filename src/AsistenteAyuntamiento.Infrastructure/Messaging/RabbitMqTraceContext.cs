using System.Diagnostics;
using System.Text;

namespace AsistenteAyuntamiento.Infrastructure.Messaging;

/// <summary>
/// W3C trace context over AMQP headers (<c>traceparent</c>, <c>tracestate</c>; see docs/messaging-contract.md).
/// Publishers inject the current activity; consumers continue the producer's trace, whether it was a .NET service or
/// the Go scraper. Done explicitly so it does not depend on RabbitMQ.Client's own instrumentation being enabled.
/// </summary>
public static class RabbitMqTraceContext
{
    public const string ActivitySourceName = "AsistenteAyuntamiento.Messaging";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>Adds the current trace context to <paramref name="headers"/> (no-op without an activity).</summary>
    public static IDictionary<string, object?> Inject(IDictionary<string, object?>? headers = null)
    {
        headers ??= new Dictionary<string, object?>();
        var activity = Activity.Current;
        if (activity is null) return headers;

        DistributedContextPropagator.Current.Inject(activity, headers, static (carrier, key, value) =>
        {
            if (carrier is IDictionary<string, object?> dictionary)
            {
                dictionary[key] = value;
            }
        });
        return headers;
    }

    /// <summary>Starts the consumer span of one delivery as a child of the producer's context, if there is one.</summary>
    public static Activity? StartProcessActivity(string queue, IDictionary<string, object?>? headers)
    {
        var parent = Extract(headers);
        // RabbitMQ.Client may already have started a deliver activity in the same trace: nest under it then.
        var parentContext = parent is { } remote && Activity.Current?.TraceId != remote.TraceId ? remote : default;

        var activity = ActivitySource.StartActivity($"{queue} process", ActivityKind.Consumer, parentContext);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", queue);
        return activity;
    }

    public static ActivityContext? Extract(IDictionary<string, object?>? headers)
    {
        if (headers is null) return null;

        DistributedContextPropagator.Current.ExtractTraceIdAndState(headers, static (object? carrier, string name, out string? value, out IEnumerable<string>? values) =>
        {
            values = null;
            value = carrier is IDictionary<string, object?> dictionary && dictionary.TryGetValue(name, out var raw)
                ? raw switch
                {
                    byte[] bytes => Encoding.UTF8.GetString(bytes),
                    string text => text,
                    _ => null,
                }
                : null;
        }, out var traceParent, out var traceState);

        return ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var context) ? context : null;
    }
}
