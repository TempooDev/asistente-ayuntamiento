using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Retrieval;

namespace AsistenteAyuntamiento.Application.Features.Chat.Pipelines;

/// <summary>
/// The "Fuentes consultadas" section, built from the documents sent to the model and appended after its answer. Asking
/// the model for it was not enough: small models (llama3.2) often leave it out or invent the links.
/// </summary>
public static class SourceCitations
{
    public const string Heading = "### Fuentes consultadas";

    /// <summary>One line per document, in the given order; an empty string when there is none.</summary>
    public static string Format(IEnumerable<(string DocumentId, string Title, string Url)> documents)
    {
        var lines = documents
            .Where(d => !string.IsNullOrWhiteSpace(d.DocumentId))
            .DistinctBy(d => d.DocumentId)
            .Select(d => string.IsNullOrWhiteSpace(d.Title) || d.Title == d.DocumentId
                ? $"- [{d.DocumentId}]({d.Url})"
                : $"- [{LinkText(d.Title)}]({d.Url}) ({d.DocumentId})")
            .ToList();

        return lines.Count == 0 ? string.Empty : $"\n\n{Heading}\n{string.Join('\n', lines)}";
    }

    /// <summary>Baseline: the chunks found in <see cref="VectorCollection.DocumentChunks"/>.</summary>
    public static string FromHits(IEnumerable<VectorSearchHit> hits) => Format(hits.Select(hit =>
    {
        var documentId = hit.GetString(VectorPayloadKeys.DocumentId);
        return (documentId, hit.GetString(VectorPayloadKeys.Title), BaselineRagPipeline.GetPublicUrl(hit.GetString(VectorPayloadKeys.Source), documentId));
    }));

    /// <summary>Hierarchical: the parent documents of the retrieved fragments.</summary>
    public static string FromRetrieval(IEnumerable<RetrievalResult> results) => Format(results
        .Where(r => r.Bulletin is not null)
        .Select(r => (r.ParentDocumentId, r.ParentTitle, BaselineRagPipeline.GetPublicUrl(r.Bulletin!.Value.ToString(), r.ParentDocumentId))));

    /// <summary>Titles are free text: brackets and line breaks would break the Markdown link.</summary>
    private static string LinkText(string title) =>
        title.ReplaceLineEndings(" ").Replace("[", "(", StringComparison.Ordinal).Replace("]", ")", StringComparison.Ordinal).Trim();
}
