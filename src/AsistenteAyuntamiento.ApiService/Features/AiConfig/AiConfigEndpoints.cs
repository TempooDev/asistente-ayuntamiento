using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AsistenteAyuntamiento.ApiService.Features.AiConfig;

public static class AiConfigEndpoints
{
    public static void MapAiConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings")
            .RequireAuthorization();

        group.MapGet("/ai", async (AiConfigurationService service) =>
        {
            var config = await service.GetConfigurationAsync();
            return Results.Ok(config);
        });

        group.MapPut("/ai", async (SaveAiConfigurationDto dto, AiConfigurationService service) =>
        {
            await service.SaveConfigurationAsync(dto);
            return Results.Ok();
        });
    }
}
