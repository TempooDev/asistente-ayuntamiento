using System.Net.Http.Json;

namespace AsistenteAyuntamiento.Web.Client;

public class BlobInfo
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public bool IsProcessed { get; set; }
}

public class IngestionApiClient
{
    private readonly HttpClient _httpClient;

    public IngestionApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BlobInfo[]> GetBlobsAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<BlobInfo[]>("/api/ingestion/blobs", cancellationToken) ?? Array.Empty<BlobInfo>();
    }

    public async Task<string> ProcessBlobAsync(string blobPath, string source, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/ingestion/process-blob", new { BlobPath = blobPath, Source = source }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProcessResponse>(cancellationToken: cancellationToken);
        return result?.Message ?? "Procesado correctamente.";
    }

    private class ProcessResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}
