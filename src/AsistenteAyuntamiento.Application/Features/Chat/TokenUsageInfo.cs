namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>Token consumption of a single AI request.</summary>
public sealed record TokenUsageInfo(
    int InputTokens,
    int OutputTokens,
    int TotalTokens)
{
    public static readonly TokenUsageInfo Empty = new(0, 0, 0);

    public static TokenUsageInfo Of(int inputTokens, int outputTokens) => new(inputTokens, outputTokens, inputTokens + outputTokens);

    /// <summary>Rough estimate (four characters per token) for providers that report no usage.</summary>
    public static int Estimate(string text) => (text.Length + 3) / 4;
}
