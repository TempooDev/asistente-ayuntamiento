using AsistenteAyuntamiento.Application.Features.Chat;
using FluentAssertions;
using OpenAI.Chat;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>Token usage as each Semantic Kernel connector reports it (found missing in the LLM observability spike).</summary>
public class TokenUsageExtractorTests
{
    [Fact]
    public void Reads_the_openai_chat_token_usage()
    {
        var usage = OpenAIChatModelFactory.ChatTokenUsage(outputTokenCount: 9, inputTokenCount: 812, totalTokenCount: 821);

        var result = TokenUsageExtractor.Extract(new Dictionary<string, object?> { ["Usage"] = usage });

        result.Should().Be(TokenUsageInfo.Of(812, 9));
    }

    [Fact]
    public void Streaming_chunks_without_usage_report_nothing()
    {
        TokenUsageExtractor.Extract(new Dictionary<string, object?> { ["Usage"] = null, ["FinishReason"] = null })
            .Should().Be(TokenUsageInfo.Empty);
    }

    [Fact]
    public void Reads_the_gemini_flat_metadata()
    {
        var result = TokenUsageExtractor.Extract(new Dictionary<string, object?> { ["PromptTokenCount"] = 120, ["CandidatesTokenCount"] = 30, ["TotalTokenCount"] = 150 });

        result.Should().Be(TokenUsageInfo.Of(120, 30));
    }

    [Fact]
    public void Reads_the_ollama_flat_metadata()
    {
        TokenUsageExtractor.Extract(new Dictionary<string, object?> { ["prompt_eval_count"] = 50, ["eval_count"] = 20 })
            .Should().Be(TokenUsageInfo.Of(50, 20));
    }
}
