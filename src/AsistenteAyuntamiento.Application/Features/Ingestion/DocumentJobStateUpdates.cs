using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// Updates one pipeline's status of a document. The baseline and hierarchical workers may update the same row at
/// the same time: a concurrent change (row version) or a concurrent insert is retried on fresh data, so neither
/// worker's status is lost and the summary is computed from both.
/// </summary>
public static class DocumentJobStateUpdates
{
    private const int MaxAttempts = 4;

    /// <returns>The document's state after the change (its <see cref="DocumentJobState.Status"/> is the summary).</returns>
    public static Task<DocumentJobState> SetPipelineStatusAsync(
        IAppDbContext dbContext,
        string documentId,
        PipelineType pipeline,
        JobStatus status,
        string? error,
        CancellationToken cancellationToken) =>
        UpdateAsync(dbContext, documentId, state => state.SetPipelineStatus(pipeline, status, error), cancellationToken);

    /// <summary>
    /// Marks as <see cref="JobStatus.Queued"/> the pipelines a message was published to that have no status yet. The
    /// scraper publishes every document to both queues; without this the slower pipeline stays unrequested (null)
    /// until it picks the message up, and the summary shows the document as completed when only the faster one is.
    /// A pipeline that already has a status (processing, completed, failed) is left alone.
    /// </summary>
    /// <returns>The state after the change, or null when every pipeline already had a status.</returns>
    public static async Task<DocumentJobState?> MarkQueuedIfUnsetAsync(
        IAppDbContext dbContext,
        string documentId,
        IReadOnlyCollection<PipelineType> pipelines,
        CancellationToken cancellationToken)
    {
        if (pipelines.Count == 0)
        {
            return null;
        }

        var changed = false;
        var state = await UpdateAsync(dbContext, documentId, state =>
        {
            changed = false;
            foreach (var pipeline in pipelines)
            {
                var current = pipeline == PipelineType.Hierarchical ? state.HierarchicalStatus : state.BaselineStatus;
                if (current is null)
                {
                    state.SetPipelineStatus(pipeline, JobStatus.Queued);
                    changed = true;
                }
            }
        }, cancellationToken);

        return changed ? state : null;
    }

    private static async Task<DocumentJobState> UpdateAsync(
        IAppDbContext dbContext,
        string documentId,
        Action<DocumentJobState> apply,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var state = await dbContext.DocumentJobStates.FirstOrDefaultAsync(j => j.DocumentId == documentId, cancellationToken);
            if (state is null)
            {
                state = new DocumentJobState { DocumentId = documentId };
                dbContext.DocumentJobStates.Add(state);
            }
            apply(state);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return state;
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Concurrency conflict or the other worker inserted the row first: reload and apply again.
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
