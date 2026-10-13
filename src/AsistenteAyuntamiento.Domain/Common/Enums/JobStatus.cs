namespace AsistenteAyuntamiento.Domain.Common.Enums;

/// <summary>
/// Ingestion state of a document. Stored as text (same values as the former string constants).
/// </summary>
public enum JobStatus
{
    Pending,
    Queued,
    Processing,
    Completed,
    Failed,

    /// <summary>PostgreSQL was committed but the vector store write failed; a retry re-syncs the vectors.</summary>
    VectorSyncFailed
}
