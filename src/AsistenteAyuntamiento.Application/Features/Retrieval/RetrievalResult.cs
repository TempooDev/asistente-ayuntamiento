using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Retrieval;

/// <summary>
/// A retrieved child fragment with its parent document. The parent's bulletin id, title, bulletin and issuing body
/// let the generation cite the norm ("Fuentes consultadas") instead of a database id.
/// </summary>
public record RetrievalResult(
    long FragmentId,
    string ChunkText,
    long ParentId,
    string ParentFullText,
    double RrfScore,
    string ParentDocumentId = "",
    string ParentTitle = "",
    BulletinType? Bulletin = null,
    string? IssuingBody = null,
    DateTime? PublicationDate = null);
