namespace AsistenteAyuntamiento.Worker.Services;

public interface IFragmentEnrichmentService
{
    Task<(string EnrichedText, int LlmCalls, int LlmTokens)> EnrichFragmentAsync(
        string bulletin,
        string issuingBody,
        string normTitle,
        string normSection,
        string subSection,
        string originalText,
        CancellationToken cancellationToken = default);
}
