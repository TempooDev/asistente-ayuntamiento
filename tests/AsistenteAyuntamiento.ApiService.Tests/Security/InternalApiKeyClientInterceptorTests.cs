using AsistenteAyuntamiento.ApiService.Protos;
using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.ApiService.Tests.Security;

/// <summary>Calls to the Go scraper carry the internal key that its command service requires.</summary>
public class InternalApiKeyClientInterceptorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Outgoing_scraper_calls_carry_the_internal_key()
    {
        var interceptor = new InternalApiKeyClientInterceptor(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [InternalApiKeyInterceptor.ConfigurationKey] = "secret" })
            .Build());
        var recorder = new RecordingInvoker();

        await new ScraperCommandService.ScraperCommandServiceClient(recorder.Intercept(interceptor))
            .ForceScrapeAsync(new ForceScrapeRequest { Provider = "BOE" });

        Assert.Equal("secret", recorder.Headers?.GetValue(InternalApiKeyInterceptor.HeaderName));
    }

    [Fact]
    public void The_api_registers_the_interceptor_for_the_scraper_client()
    {
        Assert.NotNull(factory.Services.GetService<InternalApiKeyClientInterceptor>());
    }

    private sealed class RecordingInvoker : CallInvoker
    {
        public Metadata? Headers { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Headers = options.Headers;
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult((TResponse)(object)new ForceScrapeResponse { Success = true }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
