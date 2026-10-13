namespace AsistenteAyuntamiento.Application.Features.Chat.Telemetry;

/// <summary>Values of <c>app.llm.operation</c>: what an LLM call is for, to see where the cost goes.</summary>
public static class LlmOperations
{
    public const string Answer = "answer";
    public const string QueryExpansion = "query_expansion";
    public const string Enrichment = "enrichment";
    public const string HistoryAnalysis = "history_analysis";
}
