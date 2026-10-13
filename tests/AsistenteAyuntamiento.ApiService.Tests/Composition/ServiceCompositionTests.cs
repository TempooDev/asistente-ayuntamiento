using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Users;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.ApiService.Tests.Composition;

/// <summary>The API container builds the services that hubs and endpoints resolve per request.</summary>
public class ServiceCompositionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(typeof(IAiChatService))]
    [InlineData(typeof(IArenaStreamingService))]
    [InlineData(typeof(IWinningPipelineSelector))]
    [InlineData(typeof(IShadowTestScheduler))]
    [InlineData(typeof(IChatSessionService))]
    [InlineData(typeof(IArenaService))]
    [InlineData(typeof(IIngestionAdminService))]
    [InlineData(typeof(IUserPreferenceService))]
    public void Scoped_service_resolves(Type serviceType)
    {
        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }

    [Fact]
    public void One_rag_pipeline_is_registered_per_pipeline_type()
    {
        using var scope = factory.Services.CreateScope();

        var types = scope.ServiceProvider.GetServices<IRagPipeline>().Select(p => p.Type).ToList();

        Assert.Equal([PipelineType.Baseline, PipelineType.Hierarchical], types.Order());
    }
}
