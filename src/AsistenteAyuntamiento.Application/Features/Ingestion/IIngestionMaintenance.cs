namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>Bulk maintenance of ingestion tables (Infrastructure implementation, raw SQL).</summary>
public interface IIngestionMaintenance
{
    /// <summary>Deletes every baseline chunk and every job state.</summary>
    Task ClearBaselineDataAsync(CancellationToken cancellationToken = default);
}
