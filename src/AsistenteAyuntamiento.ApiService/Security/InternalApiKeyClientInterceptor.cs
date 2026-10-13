using Grpc.Core;
using Grpc.Core.Interceptors;

namespace AsistenteAyuntamiento.ApiService.Security;

/// <summary>
/// Adds the shared internal key (<c>Internal:GrpcApiKey</c>) to calls made to the Go scraper, which rejects
/// commands without it. The counterpart of <see cref="InternalApiKeyInterceptor"/>.
/// </summary>
public class InternalApiKeyClientInterceptor(IConfiguration configuration) : Interceptor
{
    private readonly string? _key = configuration[InternalApiKeyInterceptor.ConfigurationKey];

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        if (string.IsNullOrEmpty(_key))
        {
            return continuation(request, context);
        }

        var headers = context.Options.Headers ?? new Metadata();
        headers.Add(InternalApiKeyInterceptor.HeaderName, _key);
        var options = context.Options.WithHeaders(headers);
        return continuation(request, new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options));
    }
}
