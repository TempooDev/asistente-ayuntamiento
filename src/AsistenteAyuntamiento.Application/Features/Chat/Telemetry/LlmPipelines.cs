namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>Values of <c>app.pipeline</c>: which part of the product made an LLM call.</summary>
public static class LlmPipelines
{
    public const string Baseline = "baseline";
    public const string Hierarchical = "hierarchical";
    public const string ArenaAlfa = "arena_alfa";
    public const string ArenaBeta = "arena_beta";
    public const string Shadow = "shadow";
}
