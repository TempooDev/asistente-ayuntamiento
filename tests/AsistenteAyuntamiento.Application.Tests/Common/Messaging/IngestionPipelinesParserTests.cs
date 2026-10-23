using AsistenteAyuntamiento.Application.Common.Messaging;
using AsistenteAyuntamiento.Domain.Common.Enums;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Common.Messaging;

/// <summary>The API's <c>pipelineMode</c> (spec: bulk-reprocessing-pipeline, Admin Bulk Enqueue API).</summary>
public class IngestionPipelinesParserTests
{
    [Theory]
    [InlineData("BASELINE", IngestionPipelines.Baseline)]
    [InlineData("hierarchical", IngestionPipelines.Hierarchical)]
    [InlineData("RECURSIVE", IngestionPipelines.Recursive)]
    [InlineData("Semantic", IngestionPipelines.Semantic)]
    [InlineData("BOTH", IngestionPipelines.Both)]
    [InlineData("ALL", IngestionPipelines.All)]
    [InlineData("RECURSIVE,SEMANTIC", IngestionPipelines.Recursive | IngestionPipelines.Semantic)]
    [InlineData(" baseline , semantic ", IngestionPipelines.Baseline | IngestionPipelines.Semantic)]
    [InlineData(null, IngestionPipelines.Both)]
    [InlineData("", IngestionPipelines.Both)]
    [InlineData("FIXED", IngestionPipelines.Both)]
    [InlineData("RECURSIVE,FIXED", IngestionPipelines.Both)]
    public void Parses_the_pipeline_mode(string? mode, IngestionPipelines expected) =>
        IngestionPipelinesParser.Parse(mode).Should().Be(expected);

    [Fact]
    public void All_maps_to_the_four_pipeline_types() =>
        IngestionPipelines.All.ToPipelineTypes().Should().Equal(PipelineType.Baseline, PipelineType.Hierarchical, PipelineType.Recursive, PipelineType.Semantic);
}
