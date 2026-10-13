using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace AsistenteAyuntamiento.ApiService.Features.Tenants;

/// <summary>
/// Binds every hub call to the tenant of the connection's user. <see cref="CurrentTenantService"/> reads the
/// tenant from <c>IHttpContextAccessor</c>, which is only available inside hub methods with WebSockets; with the
/// long-polling or server-sent-events fallback (proxies that block WebSockets) it is empty and every call would
/// fail. The connection's <see cref="HubCallerContext.User"/> exists for all transports.
/// </summary>
public sealed class TenantHubFilter(ICurrentTenantService tenantService) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var tenantId = BindTenant(invocationContext.Context);
        var result = await next(invocationContext);

        // A streaming method only returns its IAsyncEnumerable here; SignalR enumerates it after this filter has
        // returned, so the tenant is bound again before every item.
        return tenantId is not null && StreamItemType(result) is { } itemType
            ? Activator.CreateInstance(typeof(TenantBoundStream<>).MakeGenericType(itemType), result, tenantService, tenantId)
            : result;
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        BindTenant(context.Context);
        return next(context);
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        BindTenant(context.Context);
        return next(context, exception);
    }

    private string? BindTenant(HubCallerContext context)
    {
        // Same source as the HTTP pipeline (org_id); without it the call keeps failing closed.
        var tenantId = context.User?.FindFirst(CurrentTenantService.OrgIdClaim)?.Value;
        if (string.IsNullOrEmpty(tenantId))
        {
            return null;
        }
        tenantService.SetTenant(tenantId);
        return tenantId;
    }

    private static Type? StreamItemType(object? result) =>
        result?.GetType().GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
            ?.GetGenericArguments()[0];

    private sealed class TenantBoundStream<T>(IAsyncEnumerable<T> inner, ICurrentTenantService tenantService, string tenantId) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            tenantService.SetTenant(tenantId);
            return new Enumerator(inner.GetAsyncEnumerator(cancellationToken), tenantService, tenantId);
        }

        private sealed class Enumerator(IAsyncEnumerator<T> inner, ICurrentTenantService tenantService, string tenantId) : IAsyncEnumerator<T>
        {
            public T Current => inner.Current;

            public ValueTask<bool> MoveNextAsync()
            {
                tenantService.SetTenant(tenantId);
                return inner.MoveNextAsync();
            }

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
