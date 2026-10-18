using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class DocumentMessage
{
    [System.Text.Json.Serialization.JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("blob_path")]
    public string BlobPath { get; set; } = string.Empty;

    /// <summary>
    /// Pipelines the document was published to (<c>BASELINE</c>, <c>HIERARCHICAL</c>). The scraper sends both; absent
    /// in older messages and in the administrator's re-queues, which record their own statuses.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("pipelines")]
    public List<string>? Pipelines { get; set; }

    /// <summary>The known pipelines of <see cref="Pipelines"/>; unknown names are ignored.</summary>
    public IReadOnlyList<PipelineType> RequestedPipelines() =>
        (Pipelines ?? [])
            .Select(name => Enum.TryParse<PipelineType>(name, ignoreCase: true, out var pipeline) && Enum.IsDefined(pipeline) ? pipeline : (PipelineType?)null)
            .OfType<PipelineType>()
            .Distinct()
            .ToList();

    /// <summary>
    /// Source of a blob from its path: the folder after <c>json/</c> (<c>json/{SOURCE}/{id}.json</c>), otherwise the
    /// parent folder, otherwise <c>S3</c>.
    /// </summary>
    public static string InferSource(string? blobPath)
    {
        var parts = (blobPath ?? string.Empty).Split('/');
        if (blobPath?.StartsWith("json/") == true && parts.Length >= 3)
        {
            return parts[1];
        }

        return parts.Length > 2 ? parts[^2] : (parts.Length == 2 ? parts[0] : "S3");
    }
}
