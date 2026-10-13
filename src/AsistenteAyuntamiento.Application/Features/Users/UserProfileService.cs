using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Shared.Features.Users;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Users;

/// <summary>
/// Profiles are unique per identity (<c>Auth0UserId</c>), so lookups are by the authenticated subject and ignore
/// the tenant filter on purpose (allow-listed cross-tenant query).
/// </summary>
public sealed class UserProfileService(IAppDbContext dbContext) : IUserProfileService
{
    public async Task<UserProfileDto> GetOrCreateAsync(string auth0UserId, string tenantId, string? displayName, CancellationToken cancellationToken = default) =>
        ToDto(await LoadOrCreateAsync(auth0UserId, tenantId, displayName, cancellationToken));

    public async Task<UserProfileDto> UpdateAsync(string auth0UserId, string tenantId, string? displayName, UserProfileDto changes, CancellationToken cancellationToken = default)
    {
        var profile = await LoadOrCreateAsync(auth0UserId, tenantId, displayName, cancellationToken);

        profile.FullName = changes.FullName;
        profile.Department = changes.Department;
        profile.Position = changes.Position;
        profile.PhoneNumber = changes.PhoneNumber;
        profile.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(profile);
    }

    /// <summary>Returns a tracked profile.</summary>
    private async Task<UserProfile> LoadOrCreateAsync(string auth0UserId, string tenantId, string? displayName, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(auth0UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var profile = new UserProfile { Auth0UserId = auth0UserId, TenantId = tenantId, FullName = displayName ?? string.Empty };
        try
        {
            dbContext.UserProfiles.Add(profile);
            await dbContext.SaveChangesAsync(cancellationToken);
            return profile;
        }
        catch (DbUpdateException)
        {
            // Another request created it concurrently (unique Auth0UserId).
            dbContext.UserProfiles.Entry(profile).State = EntityState.Detached;
            return await FindAsync(auth0UserId, cancellationToken)
                ?? throw new InvalidOperationException("User profile could not be created or loaded.");
        }
    }

    private Task<UserProfile?> FindAsync(string auth0UserId, CancellationToken cancellationToken) =>
        dbContext.UserProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Auth0UserId == auth0UserId, cancellationToken);

    private static UserProfileDto ToDto(UserProfile profile) => new()
    {
        Id = profile.Id,
        Auth0UserId = profile.Auth0UserId,
        FullName = profile.FullName,
        Department = profile.Department,
        Position = profile.Position,
        PhoneNumber = profile.PhoneNumber
    };
}
