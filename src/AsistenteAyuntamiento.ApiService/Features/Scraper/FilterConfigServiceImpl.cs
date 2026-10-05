using AsistenteAyuntamiento.Application.Features.Scraper;
using AsistenteAyuntamiento.ApiService.Protos;
using Grpc.Core;

namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public class FilterConfigServiceImpl(IScraperFilterService filterService) : FilterConfigService.FilterConfigServiceBase
{
    private readonly IScraperFilterService _filterService = filterService;

    public override async Task<FilterRulesResponse> GetActiveFilters(EmptyRequest request, ServerCallContext context)
    {
        try
        {
            var activeRules = await _filterService.GetActiveRulesAsync(context.CancellationToken);
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
