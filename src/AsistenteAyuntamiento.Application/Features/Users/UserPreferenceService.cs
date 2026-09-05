using Microsoft.EntityFrameworkCore;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Users;

namespace AsistenteAyuntamiento.Application.Features.Users;

public class UserPreferenceService(IAppDbContext context, ICurrentTenantService tenantService) : IUserPreferenceService
{
    public async Task<UserPreferenceDto> GetPreferencesAsync(string auth0UserId, CancellationToken cancellationToken = default)
    {
        var preference = await context.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Auth0UserId == auth0UserId, cancellationToken);

        if (preference == null)
        {
            return new UserPreferenceDto();
        }

        return new UserPreferenceDto
        {
            Topics = preference.Topics,
            Locations = preference.Locations
        };
    }

    public async Task UpdatePreferencesAsync(string auth0UserId, UserPreferenceDto dto, CancellationToken cancellationToken = default)
    {
        var preference = await context.UserPreferences
            .FirstOrDefaultAsync(p => p.Auth0UserId == auth0UserId, cancellationToken);

        if (preference == null)
        {
            preference = new UserPreference
            {
                Auth0UserId = auth0UserId,
                TenantId = tenantService.TenantId,
                Topics = dto.Topics ?? new List<string>(),
                Locations = dto.Locations ?? new List<string>()
            };
            context.UserPreferences.Add(preference);
        }
        else
        {
            preference.Topics = dto.Topics ?? new List<string>();
            preference.Locations = dto.Locations ?? new List<string>();
            preference.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MergePreferencesAsync(string auth0UserId, UserPreferenceDto extracted, CancellationToken cancellationToken = default)
    {
        var preference = await context.UserPreferences
            .FirstOrDefaultAsync(p => p.Auth0UserId == auth0UserId, cancellationToken);

        if (preference == null)
        {
            preference = new UserPreference
            {
                Auth0UserId = auth0UserId,
                TenantId = tenantService.TenantId,
                Topics = extracted.Topics ?? new List<string>(),
                Locations = extracted.Locations ?? new List<string>()
            };
            context.UserPreferences.Add(preference);
        }
        else
        {
            var newTopics = extracted.Topics?.Except(preference.Topics ?? new List<string>(), StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            var newLocations = extracted.Locations?.Except(preference.Locations ?? new List<string>(), StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();

            if (newTopics.Any() || newLocations.Any())
            {
                if (preference.Topics == null) preference.Topics = new List<string>();
                if (preference.Locations == null) preference.Locations = new List<string>();

                preference.Topics.AddRange(newTopics);
                preference.Locations.AddRange(newLocations);
                preference.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                return; // Nothing to merge
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
