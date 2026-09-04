using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Worker.Services;

public interface IFragmentEnrichmentService
{
    Task<(string EnrichedText, int LlmCalls, int LlmTokens)> EnrichFragmentAsync(
        BulletinType bulletin,
        string issuingBody,
        string normTitle,
        string normSection,
        string subSection,
        string originalText,
        CancellationToken cancellationToken = default);
}
