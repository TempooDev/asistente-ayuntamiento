using System.Net;
using System.Net.Http.Json;
using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Arena.Models;
using AsistenteAyuntamiento.Application.Features.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AsistenteAyuntamiento.ApiService.Tests.Arena;

/// <summary>A comparison with a failed side answers 502 with the typed message and no provider details.</summary>
public class ArenaCompareEndpointTests
{
    [Fact]
    public async Task A_failed_side_answers_502_with_the_typed_message()
    {
        var arena = new Mock<IArenaService>();
        arena.Setup(a => a.CompareAsync(It.IsAny<ArenaCompareRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArenaComparisonFailedException(LlmErrorCodes.QuotaExceeded));
        await using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddScoped(_ => arena.Object)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "auth0|admin");
        client.DefaultRequestHeaders.Add(TestAuthHandler.OrgHeader, "org_arena");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.OrganizationAdmin);

        var response = await client.PostAsJsonAsync("/api/arena/compare", new { query = "¿Plazos?" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(LlmErrorCodes.QuotaExceeded, problem!.Title);
        Assert.Equal(LlmErrorCodes.Description(LlmErrorCodes.QuotaExceeded), problem.Detail);
    }
}
