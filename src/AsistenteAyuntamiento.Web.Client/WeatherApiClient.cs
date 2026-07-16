using System.Net.Http.Json;

namespace AsistenteAyuntamiento.Web.Client;

public class WeatherApiClient(HttpClient httpClient)
{
    public async Task<WeatherForecast[]> GetWeatherAsync(int maxItems = 10, CancellationToken cancellationToken = default)
    {
        var forecasts = await httpClient.GetFromJsonAsync<WeatherForecast[]>(
            "/weatherforecast",
            cancellationToken);

        return forecasts is null
            ? []
            : forecasts.Take(maxItems).ToArray();
    }
}

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
