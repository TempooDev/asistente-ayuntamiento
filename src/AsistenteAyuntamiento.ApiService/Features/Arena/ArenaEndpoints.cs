using System.Security.Claims;
using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Arena.Models;

namespace AsistenteAyuntamiento.ApiService.Features.Arena;

public static class ArenaEndpoints
{
    public static void MapArenaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/arena").WithTags("Arena").RequireAuthorization();

        // Side-by-side comparison runs both pipelines (two LLM calls): admin-only, like the Arena page.
        group.MapPost("/compare", async (
            ArenaCompareRequest request,
            ClaimsPrincipal user,
            ICurrentTenantService tenantService,
            IArenaService arenaService,
            CancellationToken ct) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tenantService.TenantId)) return Results.Unauthorized();

            try
            {
                return Results.Ok(await arenaService.CompareAsync(request, tenantService.TenantId, userId, ct));
            }
            catch (ArenaComparisonFailedException ex)
            {
                // A side failed: nothing was saved and there is nothing to vote on.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway, title: ex.ErrorCode);
            }
        })
        .RequireAuthorization(Policies.OrganizationAdmin)
        .WithName("CompareArena");

        // Any authenticated user can vote, but only on battles they own.
        group.MapPost("/vote", async (
            ArenaVoteRequest request,
            ClaimsPrincipal user,
            IArenaService arenaService,
            CancellationToken ct) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            try
            {
                var response = await arenaService.VoteAsync(request, userId, ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [ex.ParamName ?? nameof(request.Winner)] = ["Valor de voto no válido."]
                });
            }
            catch (ArenaVoteConflictException)
            {
                // Votes are final.
                return Results.Conflict();
            }
        })
        .WithName("VoteArena");

        group.MapGet("/analytics", async ([AsParameters] ArenaAnalyticsRequest request, IArenaAnalyticsService analyticsService, CancellationToken ct) =>
        {
            var response = await analyticsService.GetAnalyticsAsync(request, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithName("GetArenaAnalytics");
    }
}
