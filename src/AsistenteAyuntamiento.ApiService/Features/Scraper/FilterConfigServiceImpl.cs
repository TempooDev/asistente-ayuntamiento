using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.ApiService.Protos;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public class FilterConfigServiceImpl(IAppDbContext db) : FilterConfigService.FilterConfigServiceBase
{
    private readonly IAppDbContext _db = db;

    public override async Task<FilterRulesResponse> GetActiveFilters(EmptyRequest request, ServerCallContext context)
    {
        try
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
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching active filters for gRPC: {ex.Message}");
            return new FilterRulesResponse();
        }
    }
}
