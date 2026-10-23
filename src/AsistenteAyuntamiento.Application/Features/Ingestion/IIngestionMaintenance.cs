namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>Bulk maintenance of ingestion tables (Infrastructure implementation, raw SQL).</summary>
public interface IIngestionMaintenance
{
    /// <summary>
    /// Deletes every baseline chunk (<c>FixedOverlap</c>; the other flat variants' chunks stay) and clears the
    /// baseline status of every job state, keeping the other pipelines' statuses (rows left without any status are
    /// removed).
    /// </summary>
    Task ClearBaselineDataAsync(CancellationToken cancellationToken = default);
}
