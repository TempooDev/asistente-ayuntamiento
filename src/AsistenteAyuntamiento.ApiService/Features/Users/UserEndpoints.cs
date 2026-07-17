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
        group.MapGet("/me", async (AppDbContext db, ClaimsPrincipal user, AsistenteAyuntamiento.ApiService.Features.Tenants.CurrentTenantService tenantService) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(auth0Id)) return Results.Unauthorized();

            var profile = await db.UserProfiles.FirstOrDefaultAsync(u => u.Auth0UserId == auth0Id);
            
            if (profile == null) 
            {
                // Create empty profile on first access
                profile = new UserProfile 
                { 
                    Auth0UserId = auth0Id,
                    TenantId = tenantService.TenantId
                };
                db.UserProfiles.Add(profile);
                await db.SaveChangesAsync();
            }

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
        group.MapPut("/me", async (AppDbContext db, ClaimsPrincipal user, [FromBody] UserProfileDto dto, AsistenteAyuntamiento.ApiService.Features.Tenants.CurrentTenantService tenantService) =>
        {
            var auth0Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(auth0Id)) return Results.Unauthorized();

            var profile = await db.UserProfiles.FirstOrDefaultAsync(u => u.Auth0UserId == auth0Id);
            
            if (profile == null)
            {
                profile = new UserProfile 
                { 
                    Auth0UserId = auth0Id,
                    TenantId = tenantService.TenantId
                };
                db.UserProfiles.Add(profile);
            }

            profile.FullName = dto.FullName;
            profile.Department = dto.Department;
            profile.Position = dto.Position;
            profile.PhoneNumber = dto.PhoneNumber;
            profile.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return Results.Ok(dto);
        });
    }
}
