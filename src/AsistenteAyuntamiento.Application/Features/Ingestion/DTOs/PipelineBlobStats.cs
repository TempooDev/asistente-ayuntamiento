namespace AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

/// <summary>Progress of one pipeline over every listed document, on that pipeline's own status.</summary>
public sealed record PipelineBlobStats(string Pipeline, int Total, int Completed, int Processing, int Queued, int Failed, int NotSent);
