namespace AsistenteAyuntamiento.Worker;

/// <summary>
/// <c>dotnet AsistenteAyuntamiento.Worker.dll --health-probe &lt;url&gt;</c> exits with 0 when the URL answers 2xx and
/// with 1 otherwise. Used as the container healthcheck in <c>docker-compose.yml</c>.
/// </summary>
public static class HealthProbe
{
    public const string Argument = "--health-probe";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task<int> RunAsync(string url, HttpMessageHandler? handler = null)
    {
        try
        {
            using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = Timeout;
            using var response = await client.GetAsync(new Uri(url));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return 1;
        }
    }
}
