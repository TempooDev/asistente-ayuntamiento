using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Shared.Features.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
            IAppDbContext db,
            ClaimsPrincipal user,
            ICurrentTenantService tenantService,
            IConfiguration config) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = tenantService.TenantId;
            if (string.IsNullOrEmpty(auth0Id) || string.IsNullOrEmpty(tenantId)) return Results.Unauthorized();

            var profile = await GetOrCreateProfileAsync(db, auth0Id, tenantId, user, config);

            return Results.Ok(new UserProfileDto
            {
                Id = profile.Id,
                Auth0UserId = profile.Auth0UserId,
                FullName = profile.FullName,
                Department = profile.Department,
                Position = profile.Position,
                PhoneNumber = profile.PhoneNumber
            });
        });

        // Update current user profile
        group.MapPut("/me", async (
            IAppDbContext db,
            ClaimsPrincipal user,
            [FromBody] UserProfileDto dto,
            ICurrentTenantService tenantService,
            IConfiguration config) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = tenantService.TenantId;
            if (string.IsNullOrEmpty(auth0Id) || string.IsNullOrEmpty(tenantId)) return Results.Unauthorized();

            var profile = await GetOrCreateProfileAsync(db, auth0Id, tenantId, user, config);

            profile.FullName = dto.FullName;
            profile.Department = dto.Department;
            profile.Position = dto.Position;
            profile.PhoneNumber = dto.PhoneNumber;
            profile.UpdatedAt = DateTime.UtcNow;

            db.UserProfiles.Update(profile);
            await db.SaveChangesAsync();

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
            IServiceScopeFactory scopeFactory,
            ICurrentTenantService tenantService,
            ILoggerFactory loggerFactory,
            ClaimsPrincipal user) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = tenantService.TenantId;
            if (string.IsNullOrEmpty(auth0Id) || string.IsNullOrEmpty(tenantId)) return Results.Unauthorized();

            // Runs after the request ends, when HttpContext is gone: the tenant must be set explicitly.
            // TODO(audit-remediation 2.12): move to BackgroundWorkQueue.
            _ = Task.Run(async () =>
            {
                try
                {
                    tenantService.SetTenant(tenantId);
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IHistoryAnalyzerService>();
                    await service.AnalyzeAndMergeUserHistoryAsync(auth0Id);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger("UserEndpoints").LogError(ex, "History analysis failed for {User}", auth0Id);
                }
            });

            return Results.Accepted();
        });
    }

    /// <summary>
    /// Profiles are unique per identity (<c>Auth0UserId</c>), so the lookup is by the authenticated
    /// subject and ignores the tenant filter on purpose (allow-listed cross-tenant query).
    /// </summary>
    private static async Task<UserProfile> GetOrCreateProfileAsync(
        IAppDbContext db,
        string auth0Id,
        string tenantId,
        ClaimsPrincipal user,
        IConfiguration config,
        bool asNoTracking = true)
    {
        var query = db.UserProfiles.IgnoreQueryFilters().AsQueryable();
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        var profile = await query.FirstOrDefaultAsync(u => u.Auth0UserId == auth0Id);
        if (profile != null)
        {
            return profile;
        }

        // Try to get name from standard claims or custom namespaced claims
        var namespacePrefix = config["Auth0:CustomClaimsNamespace"];
        var nameClaim = user.FindFirst("name")?.Value
                     ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value
                     ?? user.FindFirst($"{namespacePrefix}/name")?.Value;

        // Create empty profile on first access
        profile = new UserProfile
        {
            Auth0UserId = auth0Id,
            TenantId = tenantId,
            FullName = nameClaim ?? string.Empty
        };

        try
        {
            db.UserProfiles.Add(profile);
            await db.SaveChangesAsync();

            if (asNoTracking)
            {
                // Detach so it behaves like AsNoTracking
                db.UserProfiles.Entry(profile).State = EntityState.Detached;
            }

            return profile;
        }
        catch (DbUpdateException)
        {
            // A race condition occurred: another request already created the profile.
            db.UserProfiles.Entry(profile).State = EntityState.Detached;

            return await query.FirstOrDefaultAsync(u => u.Auth0UserId == auth0Id)
                ?? throw new InvalidOperationException("User profile could not be created or loaded.");
        }
    }
}
