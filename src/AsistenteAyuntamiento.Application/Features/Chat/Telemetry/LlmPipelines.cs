namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>Values of <c>app.pipeline</c>: which part of the product made an LLM call.</summary>
public static class LlmPipelines
{
    public const string Baseline = "baseline";
    public const string Hierarchical = "hierarchical";
    public const string Recursive = "recursive";
    public const string Semantic = "semantic";
    public const string ArenaAlfa = "arena_alfa";
    public const string ArenaBeta = "arena_beta";
    public const string Shadow = "shadow";

    /// <summary>Document ingestion (synthetic questions for the hierarchical fragments).</summary>
    public const string Ingestion = "ingestion";

    /// <summary>Offline answers of the RAGAS evaluation runner, kept apart from real users' calls.</summary>
    public const string Evaluation = "evaluation";

    /// <summary>User profile work (interests extracted from the chat history).</summary>
    public const string Profile = "profile";
}
