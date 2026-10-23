using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Infrastructure.Features.Ingestion;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AsistenteAyuntamiento.Worker.Tests;

/// <summary><c>WORKER_PIPELINE_MODE</c> selects exactly one ingestion consumer (spec: bulk-reprocessing-pipeline).</summary>
public class WorkerPipelineModeTests
{
    [Theory]
    [InlineData("BASELINE", PipelineType.Baseline)]
    [InlineData("hierarchical", PipelineType.Hierarchical)]
    [InlineData("Recursive", PipelineType.Recursive)]
    [InlineData("SEMANTIC", PipelineType.Semantic)]
    [InlineData(null, PipelineType.Baseline)]
    [InlineData("", PipelineType.Baseline)]
    [InlineData("FIXED", PipelineType.Baseline)]
    [InlineData("4", PipelineType.Baseline)]
    public void Parses_the_mode(string? value, PipelineType expected) =>
        Assert.Equal(expected, WorkerPipelineMode.Parse(value));

    [Theory]
    [InlineData("BASELINE", typeof(BaselineIngestionConsumer))]
    [InlineData("HIERARCHICAL", typeof(HierarchicalIngestionConsumer))]
    [InlineData("RECURSIVE", typeof(RecursiveIngestionConsumer))]
    [InlineData("semantic", typeof(SemanticIngestionConsumer))]
    [InlineData("unknown", typeof(BaselineIngestionConsumer))]
    public void Registers_only_the_consumer_of_the_mode(string mode, Type expected)
    {
        using var factory = new ModeFactory(mode);
        _ = factory.Server;

        Assert.Equal([expected], factory.Consumers);
    }

    private sealed class ModeFactory(string mode) : HealthEndpointTests.WorkerFactory
    {
        public List<Type> Consumers { get; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("WORKER_PIPELINE_MODE", mode);
            // Runs before the base removes the consumers, so they can be recorded.
            builder.ConfigureTestServices(services => Consumers.AddRange(services
                .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name.EndsWith("IngestionConsumer", StringComparison.Ordinal) == true)
                .Select(d => d.ImplementationType!)));
            base.ConfigureWebHost(builder);
        }
    }
}
