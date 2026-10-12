using AsistenteAyuntamiento.ApiService.Protos;
using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;

namespace AsistenteAyuntamiento.ApiService.Tests.Security;

/// <summary>
/// The internal FilterConfigService only answers callers that present the shared key (spec: api-authorization).
/// </summary>
public class InternalGrpcAuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private FilterConfigService.FilterConfigServiceClient CreateClient()
    {
        var channel = GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler()
        });
        return new FilterConfigService.FilterConfigServiceClient(channel);
    }

    [Fact]
    public async Task Call_with_the_internal_key_returns_the_rules()
    {
        var headers = new Metadata { { InternalApiKeyInterceptor.HeaderName, ApiFactory.InternalGrpcKey } };

        var response = await CreateClient().GetActiveFiltersAsync(new EmptyRequest(), headers);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task Call_without_the_internal_key_is_unauthenticated()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await CreateClient().GetActiveFiltersAsync(new EmptyRequest()));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task Call_with_a_wrong_internal_key_is_unauthenticated()
    {
        var headers = new Metadata { { InternalApiKeyInterceptor.HeaderName, "wrong-key" } };

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await CreateClient().GetActiveFiltersAsync(new EmptyRequest(), headers));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }
}
