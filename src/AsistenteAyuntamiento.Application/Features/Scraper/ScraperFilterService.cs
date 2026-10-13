using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Scraper.DTOs;
using AsistenteAyuntamiento.Domain.Features.Scraper;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Scraper;

public class ScraperFilterService : IScraperFilterService
{
    private readonly IAppDbContext _dbContext;

    public ScraperFilterService(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ScraperFilterRule>> GetAllRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScraperFilterRules.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<ScraperFilterRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScraperFilterRules.FindAsync([id], cancellationToken);
    }

    public async Task<ScraperFilterRule> CreateRuleAsync(CreateFilterRuleDto dto, CancellationToken cancellationToken = default)
    {
        var rule = new ScraperFilterRule
        {
            Provider = dto.Provider,
            FilterType = dto.FilterType,
            Value = dto.Value,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ScraperFilterRules.Add(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<bool> UpdateRuleAsync(int id, UpdateFilterRuleDto dto, CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.ScraperFilterRules.FindAsync([id], cancellationToken);
        if (rule == null) return false;

        rule.Provider = dto.Provider;
        rule.FilterType = dto.FilterType;
        rule.Value = dto.Value;
        rule.IsActive = dto.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.ScraperFilterRules.FindAsync([id], cancellationToken);
        if (rule == null) return false;

        _dbContext.ScraperFilterRules.Remove(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<ScraperFilterRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScraperFilterRules
            .Where(r => r.IsActive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
