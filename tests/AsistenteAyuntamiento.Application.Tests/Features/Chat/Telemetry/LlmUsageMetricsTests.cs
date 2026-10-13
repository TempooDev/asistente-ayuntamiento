using System.Diagnostics.Metrics;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat.Telemetry;

/// <summary>GenAI metrics, labels and cost (spec: observability-grafana, "LLM Usage, Cost and Latency Metrics").</summary>
public sealed class LlmUsageMetricsTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, double Value, Dictionary<string, object?> Tags)> _measurements = [];
    private readonly Mock<ILogger<LlmUsageMetrics>> _logger = new();

    public LlmUsageMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == GenAiTelemetry.MeterName) listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Capture(i, v, t));
        _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Capture(i, v, t));
        _listener.Start();
    }

    private void Capture(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var tag in tags) dictionary[tag.Key] = tag.Value;
        // Other tests may record in parallel: keep only this test's tenant.
        if (Equals(dictionary.GetValueOrDefault(GenAiTelemetry.Attributes.TenantId), _tenant))
        {
            lock (_measurements) _measurements.Add((instrument.Name, value, dictionary));
        }
    }

    private readonly string _tenant = $"org_{Guid.NewGuid():N}";

    private LlmUsageMetrics CreateSut(Dictionary<string, string?>? settings = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build(), _logger.Object);

    private LlmCallMetrics Call(string provider = "openai", string model = "gpt-4o-mini", long input = 1_000, long output = 500,
        bool fallback = false, TimeSpan? ttft = null, string? error = null) =>
        new(provider, model, _tenant, LlmPipelines.Hierarchical, fallback, TimeSpan.FromMilliseconds(1500), input, output, ttft, error);

    private (string Name, double Value, Dictionary<string, object?> Tags) Single(string name) =>
        _measurements.Should().ContainSingle(m => m.Name == name).Subject;

    [Fact]
    public void Records_duration_and_tokens_with_the_genai_and_app_attributes()
    {
        CreateSut().Record(Call(ttft: TimeSpan.FromMilliseconds(200)));

        var duration = Single(GenAiTelemetry.Metrics.OperationDuration);
        duration.Value.Should().Be(1.5);
        duration.Tags.Should().Contain(new Dictionary<string, object?>
        {
            [GenAiTelemetry.Attributes.OperationName] = "chat",
            [GenAiTelemetry.Attributes.ProviderName] = "openai",
            [GenAiTelemetry.Attributes.RequestModel] = "gpt-4o-mini",
            [GenAiTelemetry.Attributes.Pipeline] = LlmPipelines.Hierarchical,
            [GenAiTelemetry.Attributes.Fallback] = false,
        });
        duration.Tags.Should().NotContainKey(GenAiTelemetry.Attributes.ErrorType);

        Single(GenAiTelemetry.Metrics.InputTokens).Value.Should().Be(1_000);
        Single(GenAiTelemetry.Metrics.OutputTokens).Tags[GenAiTelemetry.Attributes.TokenModality].Should().Be("text");
        Single(GenAiTelemetry.Metrics.TimeToFirstToken).Value.Should().Be(0.2);
    }

    [Fact]
    public void Failed_calls_carry_the_error_type_and_record_no_tokens()
    {
        CreateSut().Record(Call(input: 0, output: 0, error: "LLM_QUOTA_EXCEEDED"));

        Single(GenAiTelemetry.Metrics.OperationDuration).Tags[GenAiTelemetry.Attributes.ErrorType].Should().Be("LLM_QUOTA_EXCEEDED");
        _measurements.Should().NotContain(m => m.Name == GenAiTelemetry.Metrics.InputTokens || m.Name == GenAiTelemetry.Metrics.Cost);
    }

    [Fact]
    public void Fallback_calls_are_labelled_as_such()
    {
        CreateSut().Record(Call(provider: "openrouter", model: "meta-llama/llama-3.3-70b-instruct", fallback: true));

        var duration = Single(GenAiTelemetry.Metrics.OperationDuration);
        duration.Tags[GenAiTelemetry.Attributes.Fallback].Should().Be(true);
        duration.Tags[GenAiTelemetry.Attributes.RequestModel].Should().Be("meta-llama/llama-3.3-70b-instruct");
    }

    [Fact]
    public void Cost_comes_from_the_price_table_in_eur_per_million_tokens()
    {
        var sut = CreateSut(new()
        {
            ["Ai:Pricing:openai:gpt-4o-mini:Input"] = "0.14",
            ["Ai:Pricing:openai:gpt-4o-mini:Output"] = "0.56",
        });

        sut.Record(Call(input: 1_000_000, output: 500_000));

        Single(GenAiTelemetry.Metrics.Cost).Value.Should().BeApproximately(0.14 + 0.28, 1e-9);
    }

    [Fact]
    public void Unpriced_models_cost_zero_and_warn_once()
    {
        var sut = CreateSut();

        sut.EstimateCost("openai", "unknown-model", 1000, 1000).Should().Be(0);
        sut.EstimateCost("openai", "unknown-model", 1000, 1000).Should().Be(0);

        _logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void Empty_prices_from_the_compose_file_count_as_unpriced()
    {
        var sut = CreateSut(new()
        {
            ["Ai:Pricing:google:gemini-1.5-pro:Input"] = "",
            ["Ai:Pricing:google:gemini-1.5-pro:Output"] = "",
        });

        sut.EstimateCost("google", "gemini-1.5-pro", 1000, 1000).Should().Be(0);
        _logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void Self_hosted_ollama_models_are_free_without_a_warning()
    {
        CreateSut().EstimateCost("ollama", "llama3.2", 1000, 1000).Should().Be(0);

        _logger.Verify(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Theory]
    [InlineData("google", "gcp.gemini")]
    [InlineData("OpenAI", "openai")]
    [InlineData("openrouter", "openrouter")]
    [InlineData(null, "unknown")]
    public void Maps_configured_providers_to_genai_provider_names(string? configured, string expected) =>
        GenAiTelemetry.ProviderName(configured).Should().Be(expected);

    public void Dispose() => _listener.Dispose();
}
