using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using FluentAssertions;

namespace AsistenteAyuntamiento.Domain.Tests.Features.Ingestion;

/// <summary>Each pipeline keeps its own status; the summary shown to administrators combines them.</summary>
public class DocumentJobStateTests
{
    [Theory]
    [InlineData(null, null, JobStatus.Pending)]
    [InlineData(JobStatus.Completed, null, JobStatus.Completed)]
    [InlineData(JobStatus.Completed, JobStatus.Completed, JobStatus.Completed)]
    [InlineData(JobStatus.Completed, JobStatus.Processing, JobStatus.Processing)]
    [InlineData(JobStatus.Queued, JobStatus.Processing, JobStatus.Processing)]
    [InlineData(JobStatus.Completed, JobStatus.Queued, JobStatus.Queued)]
    [InlineData(JobStatus.Completed, JobStatus.Failed, JobStatus.Failed)]
    [InlineData(JobStatus.VectorSyncFailed, JobStatus.Failed, JobStatus.Failed)]
    [InlineData(JobStatus.VectorSyncFailed, JobStatus.Completed, JobStatus.VectorSyncFailed)]
    [InlineData(JobStatus.Pending, JobStatus.Completed, JobStatus.Pending)]
    public void Summarizes_the_pipeline_statuses(JobStatus? baseline, JobStatus? hierarchical, JobStatus expected) =>
        DocumentJobState.Summarize(baseline, hierarchical).Should().Be(expected);

    [Fact]
    public void A_pipeline_finishing_does_not_overwrite_the_other_one()
    {
        var state = new DocumentJobState { DocumentId = "BOE-A-1" };
        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.Processing);
        state.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Failed, "qdrant down");

        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed);

        state.HierarchicalStatus.Should().Be(JobStatus.Failed);
        state.Status.Should().Be(JobStatus.Failed, "a completed baseline must not hide the failed hierarchical run");
        state.ErrorMessage.Should().Be("[Hierarchical] qdrant down");
    }

    [Fact]
    public void The_error_is_cleared_once_no_pipeline_has_failed()
    {
        var state = new DocumentJobState { DocumentId = "BOE-A-1" };
        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.Failed, "timeout");

        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.Queued);

        state.ErrorMessage.Should().BeNull();
        state.Status.Should().Be(JobStatus.Queued);
    }

    [Fact]
    public void Reset_clears_both_pipelines()
    {
        var state = new DocumentJobState { DocumentId = "BOE-A-1" };
        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed);
        state.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Failed, "x");

        state.Reset();

        (state.BaselineStatus, state.HierarchicalStatus, state.Status, state.ErrorMessage)
            .Should().Be(((JobStatus?)null, (JobStatus?)null, JobStatus.Pending, (string?)null));
    }
}
