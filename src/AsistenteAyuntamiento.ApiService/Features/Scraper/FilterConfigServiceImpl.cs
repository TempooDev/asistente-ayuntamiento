using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.ApiService.Protos;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;

namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public class FilterConfigServiceImpl : FilterConfigService.FilterConfigServiceBase
{
    private readonly IAppDbContext _db;

    public FilterConfigServiceImpl(IAppDbContext db)
    {
        _db = db;
    }

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
