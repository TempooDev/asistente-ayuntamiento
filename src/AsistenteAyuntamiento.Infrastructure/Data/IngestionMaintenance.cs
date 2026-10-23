using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Infrastructure.Data;

public sealed class IngestionMaintenance(AppDbContext dbContext) : IIngestionMaintenance
{
    /// <summary>
    /// Only the baseline is forgotten: its chunks (<c>FixedOverlap</c>) are deleted, while the chunks of the other
    /// flat variants stay. Documents with another pipeline's status keep it (its data is not deleted) and their summary
    /// is recomputed from the remaining pipelines; the other rows only described baseline work and are deleted.
    /// </summary>
    public async Task ClearBaselineDataAsync(CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            // Providers without bulk operations (the in-memory one used by tests) do not support ExecuteDelete.
            dbContext.DocumentChunks.RemoveRange(await dbContext.DocumentChunks
                .Where(c => c.ChunkingStrategy == DocumentChunk.FixedOverlapStrategy)
                .ToListAsync(cancellationToken));
            await ClearBaselineStatusesAsync(cancellationToken);
            return;
        }

        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            dbContext.ChangeTracker.Clear();
            await dbContext.DocumentChunks
                .Where(c => c.ChunkingStrategy == DocumentChunk.FixedOverlapStrategy)
                .ExecuteDeleteAsync(ct);

            await dbContext.DocumentJobStates
                .Where(j => j.HierarchicalStatus == null && j.RecursiveStatus == null && j.SemanticStatus == null)
                .ExecuteDeleteAsync(ct);

            await ClearBaselineStatusesAsync(ct);
        }, cancellationToken);
    }

    /// <summary>The summary depends on up to three other statuses, so it is recomputed by the entity.</summary>
    private async Task ClearBaselineStatusesAsync(CancellationToken cancellationToken)
    {
        var states = await dbContext.DocumentJobStates
            .Where(j => j.BaselineStatus != null || (j.HierarchicalStatus == null && j.RecursiveStatus == null && j.SemanticStatus == null))
            .ToListAsync(cancellationToken);
        foreach (var state in states)
        {
            state.ClearPipeline(PipelineType.Baseline);
            if (state.HasNoPipeline)
            {
                dbContext.DocumentJobStates.Remove(state);
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
