using AsistenteAyuntamiento.Application.Features.AiConfig;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Application.Common.Interfaces;
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
