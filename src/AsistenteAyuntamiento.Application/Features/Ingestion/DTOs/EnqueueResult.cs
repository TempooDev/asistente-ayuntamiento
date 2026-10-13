namespace AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

/// <summary>Outcome of a bulk enqueue: documents published, and documents whose publish failed (left as Pending).</summary>
public sealed record EnqueueResult(int Enqueued, int Failed);
