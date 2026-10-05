using AsistenteAyuntamiento.Application.Features.Scraper.DTOs;
using AsistenteAyuntamiento.Application.Features.Scraper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using AsistenteAyuntamiento.ApiService.Protos;
using AsistenteAyuntamiento.ApiService.Features.Notifications;

namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public static class ScraperFilterEndpoints
{
    public static void MapScraperFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scraper/filters").RequireAuthorization();

        // 1. Get all active rules
        group.MapGet("/", async (IScraperFilterService service) =>
        {
            try 
            {
                var rules = await service.GetAllRulesAsync();
                return Results.Ok(rules);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching filter rules: {ex.Message}");
                return Results.Ok(new List<AsistenteAyuntamiento.Domain.Features.Scraper.ScraperFilterRule>());
            }
        });

        // 2. Get rule by id
        group.MapGet("/{id:int}", async (int id, IScraperFilterService service) =>
        {
            var rule = await service.GetRuleByIdAsync(id);
            return rule is not null ? Results.Ok(rule) : Results.NotFound();
        });

        // 3. Create rule
        group.MapPost("/", async (
            IScraperFilterService service,
            [FromBody] CreateFilterRuleDto dto) =>
        {
            var rule = await service.CreateRuleAsync(dto);
            return Results.Created($"/api/scraper/filters/{rule.Id}", rule);
        });

        // 4. Update rule
        group.MapPut("/{id:int}", async (
            int id,
            IScraperFilterService service,
            [FromBody] UpdateFilterRuleDto dto) =>
        {
            var updated = await service.UpdateRuleAsync(id, dto);
            if (!updated) return Results.NotFound();
            return Results.NoContent();
        });

        // 5. Delete rule
        group.MapDelete("/{id:int}", async (int id, IScraperFilterService service) =>
        {
            var deleted = await service.DeleteRuleAsync(id);
            if (!deleted) return Results.NotFound();
            return Results.NoContent();
        });

        // 6. Force scrape (manual trigger)
        group.MapPost("/trigger", async (
            [FromBody] TriggerScrapeDto dto,
            ScraperCommandService.ScraperCommandServiceClient client,
            ScraperStateService stateService,
            IHubContext<NotificationHub> hubContext) =>
        {
            if (stateService.IsScraping)
            {
                return Results.BadRequest("El scraper ya está en ejecución.");
            }

            var req = new ForceScrapeRequest
            {
                Provider = dto.Provider,
                StartDate = dto.StartDate ?? "",
                EndDate = dto.EndDate ?? ""
            };

            if (dto.Sections != null && dto.Sections.Any())
            {
                req.Sections.AddRange(dto.Sections);
            }

            stateService.IsScraping = true;
            stateService.ScrapeMessage = $"Extrayendo {dto.Provider}...";
            
            // Broadcast start
            await hubContext.Clients.All.SendAsync("ScraperStateChanged", new { isScraping = true, message = stateService.ScrapeMessage });

            // Run in background so we don't block the HTTP response or timeout
            _ = Task.Run(async () =>
            {
                try
                {
                    await client.ForceScrapeAsync(req);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error en scrape background: {ex.Message}");
                }
                finally
                {
                    stateService.IsScraping = false;
                    stateService.ScrapeMessage = "";
                    await hubContext.Clients.All.SendAsync("ScraperStateChanged", new { isScraping = false, message = "" });
                }
            });

            return Results.Accepted();
        });

        // 7. Get current state
        group.MapGet("/state", (ScraperStateService stateService) =>
        {
            return Results.Ok(new { isScraping = stateService.IsScraping, message = stateService.ScrapeMessage });
        });
    }
}
