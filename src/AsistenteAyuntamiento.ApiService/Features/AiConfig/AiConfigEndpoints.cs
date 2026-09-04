using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
namespace AsistenteAyuntamiento.ApiService.Features.AiConfig;

public static class AiConfigEndpoints
{
    public static void MapAiConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings")
            .RequireAuthorization();

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
