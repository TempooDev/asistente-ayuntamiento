namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>Bulk maintenance of ingestion tables (Infrastructure implementation, raw SQL).</summary>
public interface IIngestionMaintenance
{
    /// <summary>
    /// Deletes every baseline chunk and clears the baseline status of every job state, keeping hierarchical statuses
    /// (rows left without any status are removed).
    /// </summary>
    Task ClearBaselineDataAsync(CancellationToken cancellationToken = default);
}
