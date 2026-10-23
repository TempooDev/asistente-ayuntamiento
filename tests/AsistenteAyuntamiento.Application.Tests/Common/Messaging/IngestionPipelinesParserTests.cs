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
    [InlineData(" , ", IngestionPipelines.Both)]
    public void Parses_the_pipeline_mode(string? mode, IngestionPipelines expected)
    {
        IngestionPipelinesParser.TryParse(mode, out var pipelines, out var unknown).Should().BeTrue();
        (pipelines, unknown).Should().Be((expected, (string?)null));
        IngestionPipelinesParser.Parse(mode).Should().Be(expected);
    }

    [Theory]
    [InlineData("FIXED", "FIXED")]
    [InlineData("RECURSIVE,FIXED", "FIXED")]
    [InlineData("baseline, semantik ,hierarchical", "semantik")]
    public void Rejects_an_unknown_pipeline_instead_of_falling_back(string mode, string expectedUnknown)
    {
        IngestionPipelinesParser.TryParse(mode, out _, out var unknown).Should().BeFalse();
        unknown.Should().Be(expectedUnknown);

        var parse = () => IngestionPipelinesParser.Parse(mode);
        parse.Should().Throw<ArgumentException>().WithMessage($"*'{expectedUnknown}'*");
    }

    [Fact]
    public void All_maps_to_the_four_pipeline_types() =>
        IngestionPipelines.All.ToPipelineTypes().Should().Equal(PipelineType.Baseline, PipelineType.Hierarchical, PipelineType.Recursive, PipelineType.Semantic);
}
