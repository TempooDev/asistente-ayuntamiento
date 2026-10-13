using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Arena;
using AsistenteAyuntamiento.Domain.Features.Chat;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.ApiService.Tests.Tenancy;

/// <summary>
/// Tenant isolation and ownership (specs: multi-tenant-user-profiles, question-arena).
/// </summary>
public class TenantIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Authenticated_user_without_organization_is_forbidden_outside_development()
    {
        var client = factory.CreateClientAs("auth0|no-org");

        var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ai_call_history_only_contains_the_callers_tenant()
    {
        await factory.SeedAsync(db =>
        {
            db.AiCallLogs.AddRange(
                new AiCallLog { TenantId = "org_hist_A", UserId = "u1", ModelId = "model-a" },
                new AiCallLog { TenantId = "org_hist_B", UserId = "u2", ModelId = "model-b" });
            return Task.CompletedTask;
        });

        var client = factory.CreateClientAs("auth0|admin-a", "org_hist_A", Roles.OrganizationAdmin);

        var json = await client.GetFromJsonAsync<JsonElement>("/api/ai/metrics/history");

        var models = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("modelId").GetString()).ToList();
        Assert.Equal(["model-a"], models);
    }

    [Fact]
    public async Task Ai_configuration_of_another_tenant_is_not_visible()
    {
        await factory.SeedAsync(db =>
        {
            db.AiConfigurations.Add(new AiConfiguration { TenantId = "org_cfg_A", Provider = "openai", Model = "modelo-privado-org-a" });
            return Task.CompletedTask;
        });

        var client = factory.CreateClientAs("auth0|admin-b", "org_cfg_B", Roles.OrganizationAdmin);

        var json = await client.GetFromJsonAsync<JsonElement>("/api/settings/ai");

        Assert.NotEqual("modelo-privado-org-a", json.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("Alfa", BattleWinner.Alfa)]
    [InlineData("BETA", BattleWinner.Beta)]
    [InlineData("TIE", BattleWinner.Tie)]
    [InlineData("BOTH_BAD", BattleWinner.BothBad)] // as the Arena page sends it
    public async Task Owner_can_vote_on_their_battle(string vote, BattleWinner expected)
    {
        var battleId = await SeedBattleAsync("org_arena", "auth0|owner");
        var client = factory.CreateClientAs("auth0|owner", "org_arena");

        var response = await client.PostAsJsonAsync("/api/arena/vote", new { sessionId = battleId, winner = vote });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var winner = await factory.QueryAsync(db => db.ArenaBattles.IgnoreQueryFilters()
            .Where(b => b.SessionId == battleId).Select(b => b.Winner).SingleAsync());
        Assert.Equal(expected, winner);
    }

    [Theory]
    [InlineData("auth0|other-user", "org_arena")]
    [InlineData("auth0|owner", "org_other")]
    public async Task Vote_on_a_battle_of_someone_else_is_not_found_and_does_not_change_it(string userId, string orgId)
    {
        var battleId = await SeedBattleAsync("org_arena", "auth0|owner");
        var client = factory.CreateClientAs(userId, orgId);

        var response = await client.PostAsJsonAsync("/api/arena/vote", new { sessionId = battleId, winner = "Beta" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var winner = await factory.QueryAsync(db => db.ArenaBattles.IgnoreQueryFilters()
            .Where(b => b.SessionId == battleId).Select(b => b.Winner).SingleAsync());
        Assert.Equal(BattleWinner.Pending, winner);
    }

    [Fact]
    public async Task A_second_vote_is_a_conflict_and_keeps_the_first_vote_and_its_comment()
    {
        var battleId = await SeedBattleAsync("org_arena", "auth0|owner");
        var client = factory.CreateClientAs("auth0|owner", "org_arena");
        await client.PostAsJsonAsync("/api/arena/vote", new { sessionId = battleId, winner = "ALFA", optionalComment = "Más clara" });

        var response = await client.PostAsJsonAsync("/api/arena/vote", new { sessionId = battleId, winner = "BETA" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var battle = await factory.QueryAsync(db => db.ArenaBattles.IgnoreQueryFilters().SingleAsync(b => b.SessionId == battleId));
        Assert.Equal(BattleWinner.Alfa, battle.Winner);
        Assert.Equal("Más clara", battle.OptionalComment);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("42")]
    [InlineData("nope")]
    public async Task Invalid_vote_value_is_a_bad_request(string winner)
    {
        var battleId = await SeedBattleAsync("org_arena", "auth0|owner");
        var client = factory.CreateClientAs("auth0|owner", "org_arena");

        var response = await client.PostAsJsonAsync("/api/arena/vote", new { sessionId = battleId, winner });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_vote_is_unauthorized()
    {
        var client = factory.CreateClientAs();

        var response = await client.PostAsJsonAsync("/api/arena/vote", new { sessionId = Guid.NewGuid(), winner = "Alfa" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> SeedBattleAsync(string tenantId, string userId)
    {
        var battle = new ArenaBattle
        {
            TenantId = tenantId,
            UserId = userId,
            UserQuery = "¿Qué ayudas hay para el alquiler?",
            LeftSystem = PipelineType.Baseline,
            RightSystem = PipelineType.Hierarchical,
            LeftResponse = "respuesta A",
            RightResponse = "respuesta B",
        };

        await factory.SeedAsync(db =>
        {
            db.ArenaBattles.Add(battle);
            return Task.CompletedTask;
        });

        return battle.SessionId;
    }
}
