using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion;

/// <summary>Metrics are saved in their own scope and never fail the ingestion (spec: ingestion-telemetry).</summary>
public class IngestionMetricsServiceTests
{
    private readonly List<Mock<IAppDbContext>> _contexts = [];
    private readonly List<IngestionMetric> _added = [];

    private ServiceProvider BuildProvider(Exception? saveFailure = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            var metrics = new Mock<DbSet<IngestionMetric>>();
            metrics.Setup(m => m.Add(It.IsAny<IngestionMetric>())).Callback<IngestionMetric>(_added.Add);

            var db = new Mock<IAppDbContext>();
            db.SetupGet(d => d.IngestionMetrics).Returns(metrics.Object);
            var save = db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()));
            if (saveFailure is null)
            {
                save.ReturnsAsync(1);
            }
            else
            {
                save.ThrowsAsync(saveFailure);
            }

            _contexts.Add(db);
            return db.Object;
        });
        services.AddScoped<IIngestionMetricsService, IngestionMetricsService>();
        services.AddLogging();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task Metric_is_saved_through_its_own_database_context()
    {
        using var provider = BuildProvider();
        using var ingestionScope = provider.CreateScope();
        var ingestionDb = ingestionScope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var sut = ingestionScope.ServiceProvider.GetRequiredService<IIngestionMetricsService>();

        await sut.TrackIngestionAsync(PipelineType.Hierarchical, BulletinType.BOE, "BOE-A-1", 100, 3, 50, 7, 1234, CancellationToken.None);

        _added.Should().ContainSingle().Which.Should().Match<IngestionMetric>(m =>
            m.Pipeline == PipelineType.Hierarchical && m.DocumentId == "BOE-A-1" && m.ChunksGenerated == 7 && m.ProcessingDurationMs == 1234);
        _contexts.Should().HaveCount(2);
        _contexts[1].Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Mock.Get(ingestionDb).Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Failed_save_does_not_fail_the_ingestion()
    {
        using var provider = BuildProvider(new DbUpdateException("boom"));
        var sut = new IngestionMetricsService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<IngestionMetricsService>.Instance);

        var act = () => sut.TrackIngestionAsync(PipelineType.Baseline, BulletinType.BOJA, "BOJA-1", 10, 0, 0, 1, 5, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
