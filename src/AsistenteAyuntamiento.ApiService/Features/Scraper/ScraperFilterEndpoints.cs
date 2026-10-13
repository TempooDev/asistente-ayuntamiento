using AsistenteAyuntamiento.ApiService.Features.Notifications;
using AsistenteAyuntamiento.ApiService.Protos;
using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.Application.Common.Background;
using AsistenteAyuntamiento.Application.Features.Scraper;
using AsistenteAyuntamiento.Application.Features.Scraper.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public static class ScraperFilterEndpoints
{
    public static void MapScraperFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scraper/filters").RequireAuthorization(Policies.PlatformAdmin);

        // 1. Get all rules. Errors propagate as 500 (never an empty list that looks like "no filters").
        group.MapGet("/", async (IScraperFilterService service, CancellationToken cancellationToken) =>
        {
            var rules = await service.GetAllRulesAsync(cancellationToken);
            return Results.Ok(rules);
        });

        // 2. Get rule by id
        group.MapGet("/{id:int}", async (int id, IScraperFilterService service, CancellationToken cancellationToken) =>
        {
            var rule = await service.GetRuleByIdAsync(id, cancellationToken);
            return rule is not null ? Results.Ok(rule) : Results.NotFound();
        });

        // 3. Create rule
        group.MapPost("/", async (
            IScraperFilterService service,
            [FromBody] CreateFilterRuleDto dto,
            CancellationToken cancellationToken) =>
        {
            var rule = await service.CreateRuleAsync(dto, cancellationToken);
            return Results.Created($"/api/scraper/filters/{rule.Id}", rule);
        });

        // 4. Update rule
        group.MapPut("/{id:int}", async (
            int id,
            IScraperFilterService service,
            [FromBody] UpdateFilterRuleDto dto,
            CancellationToken cancellationToken) =>
        {
            var updated = await service.UpdateRuleAsync(id, dto, cancellationToken);
            if (!updated) return Results.NotFound();
            return Results.NoContent();
        });

        // 5. Delete rule
        group.MapDelete("/{id:int}", async (int id, IScraperFilterService service, CancellationToken cancellationToken) =>
        {
            var deleted = await service.DeleteRuleAsync(id, cancellationToken);
            if (!deleted) return Results.NotFound();
            return Results.NoContent();
        });

        // 6. Force scrape (manual trigger). The gRPC call can take minutes, so it runs as tracked background work.
        group.MapPost("/trigger", async (
            [FromBody] TriggerScrapeDto dto,
            ScraperStateService stateService,
            IBackgroundWorkQueue workQueue,
            IHubContext<NotificationHub> hubContext) =>
        {
            var message = $"Extrayendo {dto.Provider}...";
            if (!stateService.TryStart(message))
            {
                return Results.Conflict(new { message = "El scraper ya está en ejecución." });
            }

            var request = new ForceScrapeRequest
            {
                Provider = dto.Provider,
                StartDate = dto.StartDate ?? "",
                EndDate = dto.EndDate ?? ""
            };

            if (dto.Sections != null && dto.Sections.Any())
            {
                request.Sections.AddRange(dto.Sections);
            }

            var queued = workQueue.TryEnqueue(BackgroundWorkItem.PlatformWide("force-scrape", (services, cancellationToken) =>
                RunForceScrapeAsync(request, services, cancellationToken)));

            if (!queued)
            {
                stateService.Finish();
                return Results.Problem(
                    detail: "La cola de trabajos en segundo plano está llena. Inténtalo más tarde.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            await hubContext.Clients.Group(NotificationGroups.PlatformAdmins)
                .SendAsync("ScraperStateChanged", new ScraperStateNotification(true, message));

            return Results.Accepted();
        });

        // 7. Get current state
        group.MapGet("/state", (ScraperStateService stateService) =>
        {
            return Results.Ok(new ScraperStateNotification(stateService.IsScraping, stateService.ScrapeMessage));
        });
    }

    private static async Task RunForceScrapeAsync(ForceScrapeRequest request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var client = services.GetRequiredService<ScraperCommandService.ScraperCommandServiceClient>();
        var stateService = services.GetRequiredService<ScraperStateService>();
        var hubContext = services.GetRequiredService<IHubContext<NotificationHub>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ScraperFilterEndpoints));

        // Empty message = finished successfully; otherwise the reason is shown to the administrator.
        var outcome = string.Empty;
        try
        {
            var response = await client.ForceScrapeAsync(request, cancellationToken: cancellationToken);
            if (!response.Success)
            {
                // Another job running, invalid range or a failed job: the scraper says which.
                logger.LogWarning("Manual scrape of {Provider} was not completed: {Message}", request.Provider, response.Message);
                outcome = string.IsNullOrWhiteSpace(response.Message) ? "La extracción no se ha completado." : response.Message;
            }
            else
            {
                logger.LogInformation("Manual scrape of {Provider} finished: {Items} documents", request.Provider, response.ItemsExtracted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Manual scrape of {Provider} failed", request.Provider);
            outcome = "No se ha podido contactar con el scraper.";
        }
        finally
        {
            stateService.Finish();
            await hubContext.Clients.Group(NotificationGroups.PlatformAdmins)
                .SendAsync("ScraperStateChanged", new ScraperStateNotification(false, outcome), CancellationToken.None);
        }
    }
}
