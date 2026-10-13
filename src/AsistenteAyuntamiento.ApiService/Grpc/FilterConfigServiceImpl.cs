using AsistenteAyuntamiento.ApiService.Protos;
using AsistenteAyuntamiento.Application.Features.Scraper;
using Grpc.Core;

namespace AsistenteAyuntamiento.ApiService.Grpc;

/// <summary>
/// Serves the active filter rules to the Go scraper. Fails closed: on error it returns a gRPC error,
/// never an empty list, because the scraper reads "no rules" as "download everything".
/// </summary>
public class FilterConfigServiceImpl(IScraperFilterService filterService, ILogger<FilterConfigServiceImpl> logger) : FilterConfigService.FilterConfigServiceBase
{
    public override async Task<FilterRulesResponse> GetActiveFilters(EmptyRequest request, ServerCallContext context)
    {
        try
        {
            var activeRules = await filterService.GetActiveRulesAsync(context.CancellationToken);
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
        catch (Exception ex) when (ex is not RpcException and not OperationCanceledException)
        {
            logger.LogError(ex, "Error fetching active scraper filters for gRPC");
            throw new RpcException(new Status(StatusCode.Internal, "Could not load scraper filters."));
        }
    }
}
