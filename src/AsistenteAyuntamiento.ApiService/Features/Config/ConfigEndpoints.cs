using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace AsistenteAyuntamiento.ApiService.Features.Config;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config/auth0", (IConfiguration config) =>
        {
            return Results.Ok(new
            {
                Domain = config["Auth0:Domain"] ?? "",
                ClientId = config["Auth0:ClientId"] ?? "",
                Audience = config["Auth0:Audience"] ?? ""
            });
        });
    }
}
