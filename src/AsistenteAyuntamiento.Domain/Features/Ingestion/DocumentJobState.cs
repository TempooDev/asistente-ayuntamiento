using System.ComponentModel.DataAnnotations;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

/// <summary>
/// Ingestion state of one document. Each pipeline has its own status (<c>null</c> = never requested), and
/// <see cref="Status"/> is the summary shown to administrators, recomputed whenever a pipeline changes, so the two
/// workers no longer overwrite each other's result.
/// </summary>
public class DocumentJobState
{
    [Key]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Summary of the pipeline statuses (see <see cref="Summarize"/>).</summary>
    public JobStatus Status { get; set; } = JobStatus.Pending;

    public JobStatus? BaselineStatus { get; set; }
    public JobStatus? HierarchicalStatus { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last error, prefixed with the pipeline that reported it.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Records a pipeline's status and recomputes <see cref="Status"/>.</summary>
    public void SetPipelineStatus(PipelineType pipeline, JobStatus status, string? error = null)
    {
        if (pipeline == PipelineType.Hierarchical)
        {
            HierarchicalStatus = status;
        }
        else
        {
            BaselineStatus = status;
        }

        if (error is not null)
        {
            ErrorMessage = $"[{pipeline}] {error}";
        }
        else if (status is JobStatus.Queued or JobStatus.Processing or JobStatus.Completed && !HasFailure)
        {
            ErrorMessage = null;
        }

        Status = Summarize(BaselineStatus, HierarchicalStatus);
        LastUpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Clears both pipelines (manual reset by an administrator).</summary>
    public void Reset(JobStatus status = JobStatus.Pending)
    {
        BaselineStatus = null;
        HierarchicalStatus = null;
        ErrorMessage = null;
        Status = status;
        LastUpdatedAt = DateTime.UtcNow;
    }

    private bool HasFailure => BaselineStatus is JobStatus.Failed or JobStatus.VectorSyncFailed
        || HierarchicalStatus is JobStatus.Failed or JobStatus.VectorSyncFailed;

    /// <summary>
    /// Work in progress wins (processing, then queued); then failures (failed before vector-sync failed); then
    /// completed only if every requested pipeline completed. No pipeline requested yet means pending.
    /// </summary>
    public static JobStatus Summarize(JobStatus? baseline, JobStatus? hierarchical)
    {
        JobStatus[] requested = [.. new[] { baseline, hierarchical }.Where(s => s.HasValue).Select(s => s!.Value)];
        if (requested.Length == 0) return JobStatus.Pending;

        foreach (var status in new[] { JobStatus.Processing, JobStatus.Queued, JobStatus.Failed, JobStatus.VectorSyncFailed, JobStatus.Pending })
        {
            if (requested.Contains(status)) return status;
        }

        return requested.All(s => s == JobStatus.Completed) ? JobStatus.Completed : requested[0];
    }
}
