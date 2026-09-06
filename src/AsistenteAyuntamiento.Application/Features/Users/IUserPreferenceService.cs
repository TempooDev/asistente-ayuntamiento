namespace AsistenteAyuntamiento.Application.Features.Users;

public interface IUserPreferenceService
{
    Task<UserPreferenceDto> GetPreferencesAsync(string auth0UserId, CancellationToken cancellationToken = default);
    Task UpdatePreferencesAsync(string auth0UserId, UserPreferenceDto dto, CancellationToken cancellationToken = default);
    Task MergePreferencesAsync(string auth0UserId, UserPreferenceDto extracted, CancellationToken cancellationToken = default);
}
