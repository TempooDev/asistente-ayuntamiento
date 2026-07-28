using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Web.Client;

public class AiConfigApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AppTokenProvider _tokenProvider;
    private readonly ILogger<AiConfigApiClient> _logger;

    public AiConfigApiClient(HttpClient httpClient, AppTokenProvider tokenProvider, ILogger<AiConfigApiClient> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<AiConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings/ai");
        if (!string.IsNullOrEmpty(_tokenProvider.AccessToken))
        {
            _logger.LogInformation("AiConfigApiClient GET: Token IS PRESENT. Length: {Length}", _tokenProvider.AccessToken.Length);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider.AccessToken);
        }
        else
        {
            _logger.LogWarning("AiConfigApiClient GET: Token IS NULL or EMPTY!");
        }
        
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<AiConfigurationDto>(cancellationToken: cancellationToken) ?? new AiConfigurationDto();
        }
        else
        {
            _logger.LogError("AiConfigApiClient GET failed with status code {StatusCode}", response.StatusCode);
        }
        return new AiConfigurationDto();
    }

    public async Task SaveConfigurationAsync(SaveAiConfigurationDto dto, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings/ai")
        {
            Content = JsonContent.Create(dto)
        };
        if (!string.IsNullOrEmpty(_tokenProvider.AccessToken))
        {
            _logger.LogInformation("AiConfigApiClient PUT: Token IS PRESENT. Length: {Length}", _tokenProvider.AccessToken.Length);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider.AccessToken);
        }
        else
        {
            _logger.LogWarning("AiConfigApiClient PUT: Token IS NULL or EMPTY!");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("AiConfigApiClient PUT failed with status code {StatusCode}", response.StatusCode);
        }
        response.EnsureSuccessStatusCode();
    }
}

public class AiConfigurationDto
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "llama3.2";
    public double Temperature { get; set; } = 0.3;
    public bool HasApiKey { get; set; }
}

public class SaveAiConfigurationDto
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "llama3.2";
    public double Temperature { get; set; } = 0.3;
    public string? ApiKey { get; set; }
}
