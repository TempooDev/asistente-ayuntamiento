using AsistenteAyuntamiento.ApiService.Protos;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;

namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public class FilterConfigServiceImpl : FilterConfigService.FilterConfigServiceBase
{
    private readonly AppDbContext _db;

    public FilterConfigServiceImpl(AppDbContext db)
    {
        _db = db;
    }

    public override async Task<FilterRulesResponse> GetActiveFilters(EmptyRequest request, ServerCallContext context)
    {
        var activeRules = await _db.ScraperFilterRules
            .Where(r => r.IsActive)
            .ToListAsync(context.CancellationToken);

        var response = new FilterRulesResponse();
        
        foreach (var rule in activeRules)
        {
            response.Rules.Add(new FilterRule
            {
                Id = rule.Id,
                Provider = rule.Provider,
                FilterType = rule.FilterType,
                Value = rule.Value
            });
        }

        return response;
    }
}
