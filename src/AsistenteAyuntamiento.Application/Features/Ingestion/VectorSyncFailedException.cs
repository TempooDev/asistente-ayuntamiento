namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// PostgreSQL was committed but the vector store write failed. The consumer records <c>VectorSyncFailed</c> and the
/// message is retried, which re-syncs the vectors idempotently.
/// </summary>
public sealed class VectorSyncFailedException(string documentId, Exception innerException)
    : Exception($"Vector sync failed for document {documentId}.", innerException);
