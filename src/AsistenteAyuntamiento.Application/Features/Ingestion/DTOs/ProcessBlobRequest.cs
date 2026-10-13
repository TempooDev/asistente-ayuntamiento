namespace AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

public class ProcessBlobRequest
{
    public string BlobPath { get; set; } = string.Empty;

    /// <summary>Bulletin of the blob; when omitted it is inferred from the path (<c>json/{SOURCE}/{id}.json</c>).</summary>
    public string? Source { get; set; }

    /// <summary>The given source, or the one inferred from <see cref="BlobPath"/>.</summary>
    public string ResolveSource() => !string.IsNullOrWhiteSpace(Source) ? Source : DocumentMessage.InferSource(BlobPath);
}
