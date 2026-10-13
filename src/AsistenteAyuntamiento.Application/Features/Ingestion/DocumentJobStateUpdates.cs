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
    public static async Task<DocumentJobState> SetPipelineStatusAsync(
        IAppDbContext dbContext,
        string documentId,
        PipelineType pipeline,
        JobStatus status,
        string? error,
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
            state.SetPipelineStatus(pipeline, status, error);

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
