using AsistenteAyuntamiento.Shared.Features.Users;

namespace AsistenteAyuntamiento.Application.Features.Users;

public interface IUserProfileService
{
    /// <summary>
    /// Profile of the authenticated identity, created on first access with <paramref name="displayName"/>.
    /// Safe under concurrent first requests.
    /// </summary>
    Task<UserProfileDto> GetOrCreateAsync(string auth0UserId, string tenantId, string? displayName, CancellationToken cancellationToken = default);

    /// <summary>Updates the editable fields, creating the profile first if needed.</summary>
    Task<UserProfileDto> UpdateAsync(string auth0UserId, string tenantId, string? displayName, UserProfileDto changes, CancellationToken cancellationToken = default);
}
