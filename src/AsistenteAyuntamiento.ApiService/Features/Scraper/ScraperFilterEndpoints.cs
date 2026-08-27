using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public static class ScraperFilterEndpoints
{
    public static void MapScraperFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scraper/filters").RequireAuthorization();

        // 1. Get all active rules
        group.MapGet("/", async (AppDbContext db) =>
        {
            var rules = await db.ScraperFilterRules.ToListAsync();
            return Results.Ok(rules);
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
        group.MapPost("/trigger", async ([FromBody] TriggerScrapeDto dto, AsistenteAyuntamiento.ApiService.Protos.ScraperCommandService.ScraperCommandServiceClient client) =>
        {
            var req = new AsistenteAyuntamiento.ApiService.Protos.ForceScrapeRequest
            {
                Provider = dto.Provider
            };

            try
            {
                var response = await client.ForceScrapeAsync(req);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });
    }
}

public class TriggerScrapeDto
{
    [Required] public string Provider { get; set; } = string.Empty;
}

public class CreateFilterRuleDto
{
    [Required] public string Provider { get; set; } = string.Empty;
    [Required] public string FilterType { get; set; } = string.Empty;
    [Required] public string Value { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class UpdateFilterRuleDto
{
    [Required] public string Provider { get; set; } = string.Empty;
    [Required] public string FilterType { get; set; } = string.Empty;
    [Required] public string Value { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
