using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Infrastructure.Data;

public sealed class IngestionMaintenance(AppDbContext dbContext) : IIngestionMaintenance
{
    private const string BaselineErrorPrefix = "[Baseline]";

    /// <summary>
    /// Only the baseline is forgotten: its chunks (<c>FixedOverlap</c>) are deleted, while the chunks of the other
    /// flat variants stay. Documents with another pipeline's status keep it (its data is not deleted); the other rows
    /// only described baseline work and are deleted. The summary only reflects the production pipelines, so without the
    /// baseline it is the hierarchical status, or pending when there is none
    /// (<see cref="DocumentJobState.Summarize"/>), and is recomputed set-based, without loading any row.
    /// </summary>
    public async Task ClearBaselineDataAsync(CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            // Providers without bulk operations (the in-memory one used by tests) do not support ExecuteDelete/Update.
            dbContext.DocumentChunks.RemoveRange(await dbContext.DocumentChunks
                .Where(c => c.ChunkingStrategy == DocumentChunk.FixedOverlapStrategy)
                .ToListAsync(cancellationToken));
            foreach (var state in await dbContext.DocumentJobStates.ToListAsync(cancellationToken))
            {
                state.ClearPipeline(PipelineType.Baseline);
                if (state.HasNoPipeline) dbContext.DocumentJobStates.Remove(state);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            await dbContext.DocumentChunks
                .Where(c => c.ChunkingStrategy == DocumentChunk.FixedOverlapStrategy)
                .ExecuteDeleteAsync(ct);

            await dbContext.DocumentJobStates
                .Where(j => j.HierarchicalStatus == null && j.RecursiveStatus == null && j.SemanticStatus == null)
                .ExecuteDeleteAsync(ct);

            // DocumentJobState.Summarize(null, hierarchical) is the hierarchical status itself, or Pending without one.
            var now = DateTime.UtcNow;
            await dbContext.DocumentJobStates
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.BaselineStatus, (JobStatus?)null)
                    .SetProperty(j => j.Status, j => j.HierarchicalStatus ?? JobStatus.Pending)
                    .SetProperty(j => j.ErrorMessage, j => j.ErrorMessage != null && j.ErrorMessage.StartsWith(BaselineErrorPrefix) ? null : j.ErrorMessage)
                    .SetProperty(j => j.LastUpdatedAt, now), ct);
        }, cancellationToken);
    }
}
