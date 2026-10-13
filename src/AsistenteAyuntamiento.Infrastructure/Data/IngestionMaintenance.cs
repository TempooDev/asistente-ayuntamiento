using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Infrastructure.Data;

public sealed class IngestionMaintenance(AppDbContext dbContext) : IIngestionMaintenance
{
    private const string BaselineErrorPrefix = "[Baseline]";

    /// <summary>
    /// Only the baseline status is forgotten: documents with a hierarchical status keep it (its data is not deleted)
    /// and their summary becomes that status; the other rows only described baseline work and are deleted.
    /// </summary>
    public async Task ClearBaselineDataAsync(CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            // Providers without bulk operations (the in-memory one used by tests) do not support ExecuteDelete.
            dbContext.DocumentChunks.RemoveRange(await dbContext.DocumentChunks.ToListAsync(cancellationToken));
            var states = await dbContext.DocumentJobStates.ToListAsync(cancellationToken);
            foreach (var state in states)
            {
                if (state.HierarchicalStatus is null) dbContext.DocumentJobStates.Remove(state);
                else state.ClearPipeline(PipelineType.Baseline);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE ingestion.\"DocumentChunks\";", ct);

            await dbContext.DocumentJobStates
                .Where(j => j.HierarchicalStatus == null)
                .ExecuteDeleteAsync(ct);

            // DocumentJobState.Summarize(null, hierarchical) is the hierarchical status itself.
            var now = DateTime.UtcNow;
            await dbContext.DocumentJobStates
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.BaselineStatus, (JobStatus?)null)
                    .SetProperty(j => j.Status, j => j.HierarchicalStatus!.Value)
                    .SetProperty(j => j.ErrorMessage, j => j.ErrorMessage != null && j.ErrorMessage.StartsWith(BaselineErrorPrefix) ? null : j.ErrorMessage)
                    .SetProperty(j => j.LastUpdatedAt, now), ct);
        }, cancellationToken);
    }
}
