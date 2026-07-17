using System.Net.Http.Headers;
using System.Net.Http.Json;
using AsistenteAyuntamiento.Shared.Features.Users;

namespace AsistenteAyuntamiento.Web.Client;

public class UserApiClient
{
    private readonly HttpClient _httpClient;

    public UserApiClient(HttpClient httpClient, AppTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        if (!string.IsNullOrEmpty(tokenProvider.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.AccessToken);
        }
    }

    public async Task<UserProfileDto?> GetCurrentUserProfileAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<UserProfileDto>("/api/users/me", cancellationToken);
    }

    public async Task<UserProfileDto?> UpdateCurrentUserProfileAsync(UserProfileDto profile, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/users/me", profile, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserProfileDto>(cancellationToken: cancellationToken);
    }
}
