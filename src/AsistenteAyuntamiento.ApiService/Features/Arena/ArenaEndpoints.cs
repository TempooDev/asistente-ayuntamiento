using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Arena.Models;

namespace AsistenteAyuntamiento.ApiService.Features.Arena;

public static class ArenaEndpoints
{
    public static void MapArenaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/arena").WithTags("Arena");

        group.MapPost("/compare", async (ArenaCompareRequest request, IArenaService arenaService, CancellationToken ct) =>
        {
            var response = await arenaService.CompareAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("CompareArena");

        group.MapPost("/vote", async (ArenaVoteRequest request, IArenaService arenaService, CancellationToken ct) =>
        {
            var response = await arenaService.VoteAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("VoteArena");

        group.MapGet("/analytics", async (IArenaAnalyticsService analyticsService, CancellationToken ct) =>
        {
            var response = await analyticsService.GetAnalyticsAsync(ct);
            return Results.Ok(response);
        })
        .WithName("GetArenaAnalytics");
    }
}

