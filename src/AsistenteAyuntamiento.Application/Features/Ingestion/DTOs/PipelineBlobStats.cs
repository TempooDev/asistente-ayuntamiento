namespace AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

/// <summary>
/// Progress of one pipeline over every listed document, on that pipeline's own status. <see cref="Pending"/> counts
/// documents the pipeline knows but has not queued (for example after a reset); <see cref="NotSent"/> those it never received.
/// </summary>
public sealed record PipelineBlobStats(string Pipeline, int Total, int Completed, int Processing, int Queued, int Failed, int Pending, int NotSent);
