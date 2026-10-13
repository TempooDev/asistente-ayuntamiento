using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Application.Features.Scraper.DTOs;

namespace AsistenteAyuntamiento.Application.Features.Scraper;

public interface IScraperFilterService
{
    Task<List<ScraperFilterRule>> GetAllRulesAsync(CancellationToken cancellationToken = default);
    Task<ScraperFilterRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ScraperFilterRule> CreateRuleAsync(CreateFilterRuleDto dto, CancellationToken cancellationToken = default);
    Task<bool> UpdateRuleAsync(int id, UpdateFilterRuleDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default);
    Task<List<ScraperFilterRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);
}
