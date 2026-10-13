using AsistenteAyuntamiento.Application.Common.Background;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Shared.Features.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AsistenteAyuntamiento.Application.Features.Users;

namespace AsistenteAyuntamiento.ApiService.Features.Users;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();

        // Get current user profile
        group.MapGet("/me", async (
            IUserProfileService profiles,
            ClaimsPrincipal user,
            ICurrentTenantService tenantService,
            IConfiguration config,
            CancellationToken cancellationToken) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = tenantService.TenantId;
            if (string.IsNullOrEmpty(auth0Id) || string.IsNullOrEmpty(tenantId)) return Results.Unauthorized();

            return Results.Ok(await profiles.GetOrCreateAsync(auth0Id, tenantId, DisplayName(user, config), cancellationToken));
        });

        // Update current user profile
        group.MapPut("/me", async (
            IUserProfileService profiles,
            ClaimsPrincipal user,
            [FromBody] UserProfileDto dto,
            ICurrentTenantService tenantService,
            IConfiguration config,
            CancellationToken cancellationToken) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = tenantService.TenantId;
            if (string.IsNullOrEmpty(auth0Id) || string.IsNullOrEmpty(tenantId)) return Results.Unauthorized();

            await profiles.UpdateAsync(auth0Id, tenantId, DisplayName(user, config), dto, cancellationToken);
            return Results.Ok(dto);
        });

        // Get user preferences
        group.MapGet("/me/preferences", async (
            IUserPreferenceService service,
            ClaimsPrincipal user) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(auth0Id)) return Results.Unauthorized();

            var preferences = await service.GetPreferencesAsync(auth0Id);
            return Results.Ok(preferences);
        });

        // Update user preferences
        group.MapPut("/me/preferences", async (
            IUserPreferenceService service,
            ClaimsPrincipal user,
            [FromBody] UserPreferenceDto dto) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(auth0Id)) return Results.Unauthorized();

            await service.UpdatePreferencesAsync(auth0Id, dto);
            return Results.Ok(dto);
        });

        // Trigger history analysis
        group.MapPost("/me/preferences/analyze", (
            IBackgroundWorkQueue workQueue,
            ICurrentTenantService tenantService,
            ClaimsPrincipal user) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = tenantService.TenantId;
            if (string.IsNullOrEmpty(auth0Id) || string.IsNullOrEmpty(tenantId)) return Results.Unauthorized();

            // Runs after the request ends, when HttpContext is gone: the work item carries the tenant.
            var queued = workQueue.TryEnqueue(BackgroundWorkItem.ForTenant("analyze-user-history", tenantId, (services, cancellationToken) =>
                services.GetRequiredService<IHistoryAnalyzerService>().AnalyzeAndMergeUserHistoryAsync(auth0Id, cancellationToken)));

            return queued
                ? Results.Accepted()
                : Results.Problem(detail: "La cola de trabajos está llena. Inténtalo más tarde.", statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }

    /// <summary>Name for a new profile, from the standard claims or the Auth0 custom namespace.</summary>
    private static string? DisplayName(ClaimsPrincipal user, IConfiguration config) =>
        user.FindFirst("name")?.Value
        ?? user.FindFirst(ClaimTypes.Name)?.Value
        ?? user.FindFirst($"{config["Auth0:CustomClaimsNamespace"]}/name")?.Value;
}
