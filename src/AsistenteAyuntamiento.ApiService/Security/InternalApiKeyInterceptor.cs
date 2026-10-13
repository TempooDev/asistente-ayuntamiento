using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace AsistenteAyuntamiento.ApiService.Security;

/// <summary>
/// Protects internal gRPC services (called by the Go scraper) with a shared secret sent in the
/// <c>x-internal-key</c> metadata header. The key comes from <c>Internal:GrpcApiKey</c>.
/// </summary>
public class InternalApiKeyInterceptor(IConfiguration configuration, IHostEnvironment environment, ILogger<InternalApiKeyInterceptor> logger) : Interceptor
{
    public const string HeaderName = "x-internal-key";
    public const string ConfigurationKey = "Internal:GrpcApiKey";

    private readonly byte[]? _expectedKey = string.IsNullOrEmpty(configuration[ConfigurationKey])
        ? null
        : Encoding.UTF8.GetBytes(configuration[ConfigurationKey]!);

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return continuation(request, context);
    }

    private void EnsureAuthorized(ServerCallContext context)
    {
        if (_expectedKey is null)
        {
            // Only reachable outside Production: startup validation requires the key there.
            if (environment.IsDevelopment())
            {
                logger.LogWarning("{ConfigurationKey} is not configured; accepting internal gRPC call in Development", ConfigurationKey);
                return;
            }

            throw new RpcException(new Status(StatusCode.Unauthenticated, "Internal key not configured."));
        }

        var provided = context.RequestHeaders.GetValue(HeaderName);
        if (provided is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), _expectedKey))
        {
            logger.LogWarning("Rejected internal gRPC call to {Method}: missing or invalid {Header}", context.Method, HeaderName);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid internal key."));
        }
    }
}
