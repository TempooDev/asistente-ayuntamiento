using Microsoft.EntityFrameworkCore;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Users;

namespace AsistenteAyuntamiento.Application.Features.Users;

/// <summary>
/// Preferences of one user in one organization (unique on <c>Auth0UserId</c> + <c>TenantId</c>): the same person in
/// two organizations has two independent sets. Every query runs under the tenant filter and also names the tenant.
/// </summary>
public class UserPreferenceService(IAppDbContext context, ICurrentTenantService tenantService) : IUserPreferenceService
{
    private string RequireTenant() => tenantService.TenantId
        ?? throw new InvalidOperationException("A tenant is required to read or change user preferences.");

    public async Task<UserPreferenceDto> GetPreferencesAsync(string auth0UserId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantService.TenantId;
        if (tenantId is null)
        {
            return new UserPreferenceDto();
        }

        var preference = await context.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Auth0UserId == auth0UserId && p.TenantId == tenantId, cancellationToken);

        return preference is null
            ? new UserPreferenceDto()
            : new UserPreferenceDto { Topics = preference.Topics, Locations = preference.Locations };
    }

    public async Task UpdatePreferencesAsync(string auth0UserId, UserPreferenceDto dto, CancellationToken cancellationToken = default)
    {
        var (preference, _) = await FindOrAddAsync(auth0UserId, cancellationToken);
        preference.Topics = Distinct(dto.Topics);
        preference.Locations = Distinct(dto.Locations);
        preference.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Adds the extracted interests that are new (case-insensitive), keeping the existing ones.</summary>
    public async Task MergePreferencesAsync(string auth0UserId, UserPreferenceDto extracted, CancellationToken cancellationToken = default)
    {
        var (preference, isNew) = await FindOrAddAsync(auth0UserId, cancellationToken);
        var topics = Distinct((preference.Topics ?? []).Concat(extracted.Topics ?? []));
        var locations = Distinct((preference.Locations ?? []).Concat(extracted.Locations ?? []));

        if (!isNew
            && topics.Count == (preference.Topics?.Count ?? 0)
            && locations.Count == (preference.Locations?.Count ?? 0))
        {
            return; // Nothing new.
        }

        preference.Topics = topics;
        preference.Locations = locations;
        preference.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<(UserPreference Preference, bool IsNew)> FindOrAddAsync(string auth0UserId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var preference = await context.UserPreferences
            .FirstOrDefaultAsync(p => p.Auth0UserId == auth0UserId && p.TenantId == tenantId, cancellationToken);
        if (preference is not null)
        {
            return (preference, false);
        }

        preference = new UserPreference { Auth0UserId = auth0UserId, TenantId = tenantId, Topics = [], Locations = [] };
        context.UserPreferences.Add(preference);
        return (preference, true);
    }

    private static List<string> Distinct(IEnumerable<string>? values) =>
        (values ?? [])
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
