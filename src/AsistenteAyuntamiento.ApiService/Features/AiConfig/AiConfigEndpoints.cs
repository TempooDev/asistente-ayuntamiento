using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using AsistenteAyuntamiento.ApiService.Security;

namespace AsistenteAyuntamiento.ApiService.Features.AiConfig;

public static class AiConfigEndpoints
{
    public static void MapAiConfigEndpoints(this IEndpointRouteBuilder app)
    {
        // The tenant's AI provider and API key are managed only from the admin settings page.
        var group = app.MapGroup("/api/settings").RequireAuthorization(Policies.Admin);

        group.MapGet("/ai", async (IAiConfigurationService service) =>
        {
            var config = await service.GetConfigurationAsync();
            return Results.Ok(config);
        });

        group.MapPut("/ai", async (SaveAiConfigurationDto dto, IAiConfigurationService service) =>
        {
            await service.SaveConfigurationAsync(dto);
            return Results.Ok();
        });
    }
}
