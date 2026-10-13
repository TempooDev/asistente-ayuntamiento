using AsistenteAyuntamiento.Application.Common.Interfaces;

namespace AsistenteAyuntamiento.ApiService.Features.Tenants;

/// <summary>
/// Rejects authenticated requests that cannot be bound to a tenant (no <c>org_id</c> claim),
/// so they never fall back to a shared tenant. Anonymous requests are left to the authorization pipeline.
/// </summary>
public class TenantRequiredMiddleware(RequestDelegate next, ILogger<TenantRequiredMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICurrentTenantService tenantService)
    {
        if (context.User.Identity?.IsAuthenticated == true && string.IsNullOrEmpty(tenantService.TenantId))
        {
            logger.LogWarning("Rejected authenticated request without organization on {Path}", context.Request.Path);

            await Results.Problem(
                    title: "Organization required",
                    detail: "El usuario no pertenece a ninguna organización.",
                    statusCode: StatusCodes.Status403Forbidden)
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
