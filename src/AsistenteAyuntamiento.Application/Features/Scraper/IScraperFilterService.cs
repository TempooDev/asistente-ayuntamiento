using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Application.Features.Scraper.DTOs;

namespace AsistenteAyuntamiento.Application.Features.Scraper;

public interface IScraperFilterService
{
    Task<List<ScraperFilterRule>> GetAllRulesAsync();
    Task<ScraperFilterRule?> GetRuleByIdAsync(int id);
    Task<ScraperFilterRule> CreateRuleAsync(CreateFilterRuleDto dto);
    Task<bool> UpdateRuleAsync(int id, UpdateFilterRuleDto dto);
    Task<bool> DeleteRuleAsync(int id);
    Task<List<ScraperFilterRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);
}
