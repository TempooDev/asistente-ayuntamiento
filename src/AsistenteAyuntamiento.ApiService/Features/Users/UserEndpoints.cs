using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
using AsistenteAyuntamiento.Shared.Features.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AsistenteAyuntamiento.ApiService.Features.Users;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();

        // Get current user profile
        group.MapGet("/me", async (AppDbContext db, ClaimsPrincipal user, Tenants.CurrentTenantService tenantService, IConfiguration config) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(auth0Id)) return Results.Unauthorized();

            var profile = await GetOrCreateProfileAsync(db, auth0Id, user, tenantService, config);

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
        group.MapPut("/me", async (AppDbContext db, ClaimsPrincipal user, [FromBody] UserProfileDto dto, Tenants.CurrentTenantService tenantService, IConfiguration config) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(auth0Id)) return Results.Unauthorized();

            var profile = await GetOrCreateProfileAsync(db, auth0Id, user, tenantService, config);

            profile.FullName = dto.FullName;
            profile.Department = dto.Department;
            profile.Position = dto.Position;
            profile.PhoneNumber = dto.PhoneNumber;
            profile.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return Results.Ok(dto);
        });
    }

    private static async Task<UserProfile> GetOrCreateProfileAsync(AppDbContext db, string auth0Id, ClaimsPrincipal user, Tenants.CurrentTenantService tenantService, IConfiguration config)
    {
        try
        {
            var profile = await db.UserProfiles.FirstOrDefaultAsync(u => u.Auth0UserId == auth0Id);

            if (profile == null)
            {
                // Try to get name from standard claims or custom namespaced claims
                var namespacePrefix = config["Auth0:CustomClaimsNamespace"];
                var nameClaim = user.FindFirst("name")?.Value 
                             ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value
                             ?? user.FindFirst($"{namespacePrefix}/name")?.Value;

                // Create empty profile on first access
                profile = new UserProfile
                {
                    Auth0UserId = auth0Id,
                    TenantId = tenantService.TenantId,
                    FullName = nameClaim ?? string.Empty
                };
                db.UserProfiles.Add(profile);
                await db.SaveChangesAsync();
            }

            return profile;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting/creating user profile: {ex.Message}");
            return new UserProfile
            {
                Auth0UserId = auth0Id,
                TenantId = tenantService.TenantId,
                FullName = string.Empty
            };
        }
    }
}
