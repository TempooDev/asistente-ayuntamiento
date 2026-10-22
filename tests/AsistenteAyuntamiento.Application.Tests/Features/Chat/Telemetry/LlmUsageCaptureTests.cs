using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat.Telemetry;

public class LlmUsageCaptureTests
{
    private readonly LlmUsageMetrics _metrics = new(new ConfigurationBuilder().Build(), NullLogger<LlmUsageMetrics>.Instance);

    private static LlmCallMetrics Call(long input, long output) =>
        new("ollama", "qwen2.5", "evaluation", LlmPipelines.Evaluation, false, TimeSpan.FromSeconds(1), input, output);

    [Fact]
    public async Task Sums_the_calls_recorded_in_the_flow_while_the_scope_is_open()
    {
        _metrics.Record(Call(1, 1));

        TokenUsageInfo total;
        using (var scope = LlmUsageCapture.Begin())
        {
            _metrics.Record(Call(100, 10));
            await Task.Run(() => _metrics.Record(Call(50, 5)));
            total = scope.Total;
        }
        _metrics.Record(Call(1, 1));

        total.Should().Be(TokenUsageInfo.Of(150, 15));
    }
}
