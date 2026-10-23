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

    [Fact]
    public void Clearing_the_baseline_keeps_the_hierarchical_status_and_error()
    {
        var state = new DocumentJobState { DocumentId = "BOE-A-1" };
        state.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Failed, "llm down");
        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed);

        state.ClearPipeline(PipelineType.Baseline);

        (state.BaselineStatus, state.HierarchicalStatus, state.Status, state.ErrorMessage)
            .Should().Be(((JobStatus?)null, (JobStatus?)JobStatus.Failed, JobStatus.Failed, "[Hierarchical] llm down"));
    }

    [Fact]
    public void Clearing_a_pipeline_drops_its_own_error_and_recomputes_the_summary()
    {
        var state = new DocumentJobState { DocumentId = "BOE-A-1" };
        state.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Completed);
        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.VectorSyncFailed, "qdrant down");

        state.ClearPipeline(PipelineType.Baseline);

        (state.BaselineStatus, state.Status, state.ErrorMessage)
            .Should().Be(((JobStatus?)null, JobStatus.Completed, (string?)null));
    }

    [Fact]
    public void The_study_pipelines_keep_their_own_statuses_outside_the_summary()
    {
        var state = new DocumentJobState { DocumentId = "BOE-A-1" };
        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed);
        state.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Completed);
        state.SetPipelineStatus(PipelineType.Recursive, JobStatus.Completed);

        state.SetPipelineStatus(PipelineType.Semantic, JobStatus.Failed, "ollama down");

        (state.RecursiveStatus, state.SemanticStatus, state.Status, state.ErrorMessage)
            .Should().Be(((JobStatus?)JobStatus.Completed, (JobStatus?)JobStatus.Failed, JobStatus.Completed, "[Semantic] ollama down"),
                "a study failure must not turn the production summary into Failed");
        state.GetPipelineStatus(PipelineType.Semantic).Should().Be(JobStatus.Failed);

        state.SetPipelineStatus(PipelineType.Semantic, JobStatus.Completed);
        (state.Status, state.ErrorMessage).Should().Be((JobStatus.Completed, (string?)null));
    }

    [Theory]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Processing)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.VectorSyncFailed)]
    public void An_unconsumed_or_failed_study_queue_does_not_change_the_summary(JobStatus study)
    {
        var state = new DocumentJobState { DocumentId = "BOE-A-1" };
        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed);
        state.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Completed);

        state.SetPipelineStatus(PipelineType.Recursive, study);
        state.SetPipelineStatus(PipelineType.Semantic, study);

        state.Status.Should().Be(JobStatus.Completed);
        (state.RecursiveStatus, state.SemanticStatus).Should().Be(((JobStatus?)study, (JobStatus?)study));
    }

    [Fact]
    public void Only_study_pipelines_requested_leaves_the_summary_pending()
    {
        var state = new DocumentJobState { DocumentId = "BOE-A-1" };

        state.SetPipelineStatus(PipelineType.Recursive, JobStatus.Queued);

        (state.Status, state.HasNoPipeline).Should().Be((JobStatus.Pending, false));
    }

    [Fact]
    public void Reset_and_clear_cover_the_study_pipelines()
    {
        var state = new DocumentJobState { DocumentId = "BOE-A-1" };
        state.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Completed);
        state.SetPipelineStatus(PipelineType.Recursive, JobStatus.Completed);
        state.SetPipelineStatus(PipelineType.Semantic, JobStatus.Failed, "x");

        state.ClearPipeline(PipelineType.Semantic);
        (state.SemanticStatus, state.Status, state.ErrorMessage).Should().Be(((JobStatus?)null, JobStatus.Completed, (string?)null));

        state.Reset();
        (state.RecursiveStatus, state.HasNoPipeline).Should().Be(((JobStatus?)null, true));
    }

    [Theory]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Processing)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.VectorSyncFailed)]
    public void Without_a_baseline_the_summary_is_the_hierarchical_status(JobStatus hierarchical) =>
        // A single requested pipeline is its own summary.
        DocumentJobState.Summarize(null, hierarchical).Should().Be(hierarchical);
}
