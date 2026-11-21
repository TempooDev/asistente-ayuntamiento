using AsistenteAyuntamiento.Application.Features.Generation;
using FluentAssertions;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ai;

/// <summary>
/// The hierarchical answer's generic settings must reach every chat connector the platform can use as a real output
/// limit: OpenAI and OpenRouter read <c>max_tokens</c>, Google reads <c>max_tokens</c> and Ollama reads <c>num_predict</c>.
/// </summary>
public class OutputTokenLimitTests
{
    private static readonly Microsoft.SemanticKernel.PromptExecutionSettings Settings = ClearLanguageGenerationService.BuildSettings(0, 2_048);

    [Fact]
    public void OpenAI_and_OpenRouter_read_the_limit() =>
        OpenAIPromptExecutionSettings.FromExecutionSettings(Settings).MaxTokens.Should().Be(2_048);

    [Fact]
    public void Google_reads_the_limit() =>
        GeminiPromptExecutionSettings.FromExecutionSettings(Settings).MaxTokens.Should().Be(2_048);

    [Fact]
    public void Ollama_reads_the_limit() =>
        OllamaPromptExecutionSettings.FromExecutionSettings(Settings).NumPredict.Should().Be(2_048);

    [Fact]
    public void The_temperature_still_reaches_the_connector() =>
        OpenAIPromptExecutionSettings.FromExecutionSettings(Settings).Temperature.Should().Be(0);
}
