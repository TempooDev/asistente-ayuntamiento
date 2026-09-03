using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.SignalR;
using AsistenteAyuntamiento.ApiService.Features.Scraper.DTOs;

namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public static class ScraperFilterEndpoints
{
    public static void MapScraperFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scraper/filters").RequireAuthorization();

        // 1. Get all active rules
        group.MapGet("/", async (AppDbContext db) =>
        {
            try 
            {
                var rules = await db.ScraperFilterRules.ToListAsync();
                return Results.Ok(rules);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching filter rules: {ex.Message}");
                return Results.Ok(new List<ScraperFilterRule>());
            }
        });

        // 2. Get rule by id
        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var rule = await db.ScraperFilterRules.FindAsync(id);
            return rule is not null ? Results.Ok(rule) : Results.NotFound();
        });

        // 3. Create rule
        group.MapPost("/", async (AppDbContext db, [FromBody] CreateFilterRuleDto dto) =>
        {
            var rule = new ScraperFilterRule
            {
                Provider = dto.Provider,
                FilterType = dto.FilterType,
                Value = dto.Value,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            db.ScraperFilterRules.Add(rule);
            await db.SaveChangesAsync();

            return Results.Created($"/api/scraper/filters/{rule.Id}", rule);
        });

        // 4. Update rule
        group.MapPut("/{id:int}", async (int id, AppDbContext db, [FromBody] UpdateFilterRuleDto dto) =>
        {
            var rule = await db.ScraperFilterRules.FindAsync(id);
            if (rule is null) return Results.NotFound();

            rule.Provider = dto.Provider;
            rule.FilterType = dto.FilterType;
            rule.Value = dto.Value;
            rule.IsActive = dto.IsActive;

            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        // 5. Delete rule
        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var rule = await db.ScraperFilterRules.FindAsync(id);
            if (rule is null) return Results.NotFound();

            db.ScraperFilterRules.Remove(rule);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        // 6. Force scrape (manual trigger)
        group.MapPost("/trigger", async (
            [FromBody] TriggerScrapeDto dto, 
            AsistenteAyuntamiento.ApiService.Protos.ScraperCommandService.ScraperCommandServiceClient client,
            ScraperStateService stateService,
            IHubContext<AsistenteAyuntamiento.ApiService.Features.Notifications.NotificationHub> hubContext) =>
        {
            if (stateService.IsScraping)
            {
                return Results.BadRequest("El scraper ya está en ejecución.");
            }

            var req = new AsistenteAyuntamiento.ApiService.Protos.ForceScrapeRequest
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


