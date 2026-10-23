using System.ComponentModel.DataAnnotations;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

/// <summary>
/// Ingestion state of one document. Each pipeline has its own status (<c>null</c> = never requested), and
/// <see cref="Status"/> is the summary shown to administrators, recomputed whenever a pipeline changes, so the
/// workers never overwrite each other's result.
/// </summary>
public class DocumentJobState
{
    [Key]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Summary of the pipeline statuses (see <see cref="Summarize"/>).</summary>
    public JobStatus Status { get; set; } = JobStatus.Pending;

    public JobStatus? BaselineStatus { get; set; }
    public JobStatus? HierarchicalStatus { get; set; }
    public JobStatus? RecursiveStatus { get; set; }
    public JobStatus? SemanticStatus { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last error, prefixed with the pipeline that reported it.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>The status of one pipeline (<c>null</c> = never requested).</summary>
    public JobStatus? GetPipelineStatus(PipelineType pipeline) => pipeline switch
    {
        PipelineType.Hierarchical => HierarchicalStatus,
        PipelineType.Recursive => RecursiveStatus,
        PipelineType.Semantic => SemanticStatus,
        _ => BaselineStatus,
    };

    /// <summary>Records a pipeline's status and recomputes <see cref="Status"/>.</summary>
    public void SetPipelineStatus(PipelineType pipeline, JobStatus status, string? error = null)
    {
        Assign(pipeline, status);

        if (error is not null)
        {
            ErrorMessage = $"[{pipeline}] {error}";
        }
        else if (status is JobStatus.Queued or JobStatus.Processing or JobStatus.Completed && !HasFailure)
        {
            ErrorMessage = null;
        }

        Status = Summarize(BaselineStatus, HierarchicalStatus, RecursiveStatus, SemanticStatus);
        LastUpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Clears every pipeline (manual reset by an administrator).</summary>
    public void Reset(JobStatus status = JobStatus.Pending)
    {
        BaselineStatus = null;
        HierarchicalStatus = null;
        RecursiveStatus = null;
        SemanticStatus = null;
        ErrorMessage = null;
        Status = status;
        LastUpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Forgets one pipeline (its data was deleted): its status and its error are cleared and <see cref="Status"/> is
    /// recomputed from the other pipelines alone.
    /// </summary>
    public void ClearPipeline(PipelineType pipeline)
    {
        Assign(pipeline, null);

        if (ErrorMessage?.StartsWith($"[{pipeline}]", StringComparison.Ordinal) == true)
        {
            ErrorMessage = null;
        }

        Status = Summarize(BaselineStatus, HierarchicalStatus, RecursiveStatus, SemanticStatus);
        LastUpdatedAt = DateTime.UtcNow;
    }

    /// <summary>True when no pipeline has a status (nothing left to describe).</summary>
    public bool HasNoPipeline => BaselineStatus is null && HierarchicalStatus is null && RecursiveStatus is null && SemanticStatus is null;

    private void Assign(PipelineType pipeline, JobStatus? status)
    {
        switch (pipeline)
        {
            case PipelineType.Hierarchical: HierarchicalStatus = status; break;
            case PipelineType.Recursive: RecursiveStatus = status; break;
            case PipelineType.Semantic: SemanticStatus = status; break;
            default: BaselineStatus = status; break;
        }
    }

    private bool HasFailure => new[] { BaselineStatus, HierarchicalStatus, RecursiveStatus, SemanticStatus }
        .Any(s => s is JobStatus.Failed or JobStatus.VectorSyncFailed);

    /// <summary>
    /// Work in progress wins (processing, then queued); then failures (failed before vector-sync failed); then
    /// completed only if every requested pipeline completed. No pipeline requested yet means pending.
    /// </summary>
    public static JobStatus Summarize(params JobStatus?[] pipelines)
    {
        JobStatus[] requested = [.. pipelines.Where(s => s.HasValue).Select(s => s!.Value)];
        if (requested.Length == 0) return JobStatus.Pending;

        foreach (var status in new[] { JobStatus.Processing, JobStatus.Queued, JobStatus.Failed, JobStatus.VectorSyncFailed, JobStatus.Pending })
        {
            if (requested.Contains(status)) return status;
        }

        return requested.All(s => s == JobStatus.Completed) ? JobStatus.Completed : requested[0];
    }
}
