using AsistenteAyuntamiento.ApiService.Features.Chat;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.ApiService.Tests.Realtime;

/// <summary>
/// Chat Arena votes through a real SignalR client (spec: question-arena): votes are final, and the answer the chat
/// keeps is the one returned to the client.
/// </summary>
public class ChatHubArenaVoteTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncDisposable
{
    private const string User = "auth0|voter";
    private const string Org = "org_vote";
    private readonly List<HubConnection> _connections = [];

    private async Task<HubConnection> ConnectAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Headers[TestAuthHandler.UserHeader] = User;
                options.Headers[TestAuthHandler.OrgHeader] = Org;
            })
            .Build();
        _connections.Add(connection);
        await connection.StartAsync();
        return connection;
    }

    private async Task<Guid> SeedBattleAsync()
    {
        var battle = new ArenaBattle
        {
            TenantId = Org,
            UserId = User,
            UserQuery = "¿Plazo de la ayuda?",
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

    /// <summary>Assistant messages queued for persistence in <paramref name="chatSessionId"/>.</summary>
    private List<string> StoredAnswers(Guid chatSessionId) =>
        factory.Services.GetRequiredService<ChatMessageBuffer>().DrainAll()
            .Where(m => m.SessionId == chatSessionId && m.Role == "assistant")
            .Select(m => m.Content)
            .ToList();

    private static object Vote(Guid chatSessionId, Guid battleId, string winner) =>
        new { chatSessionId, battleId, winner };

    [Fact]
    public async Task A_tie_stores_both_answers_and_returns_what_was_stored()
    {
        var connection = await ConnectAsync();
        var chatSessionId = await connection.InvokeAsync<Guid>("CreateNewSession");
        var battleId = await SeedBattleAsync();

        var shown = await connection.InvokeAsync<string?>("VoteArenaMessage", Vote(chatSessionId, battleId, "Tie"));

        Assert.NotNull(shown);
        Assert.Contains("respuesta A", shown);
        Assert.Contains("respuesta B", shown);
        Assert.Equal([shown], StoredAnswers(chatSessionId));
    }

    [Fact]
    public async Task A_second_vote_is_rejected_and_changes_nothing()
    {
        var connection = await ConnectAsync();
        var chatSessionId = await connection.InvokeAsync<Guid>("CreateNewSession");
        var battleId = await SeedBattleAsync();

        var shown = await connection.InvokeAsync<string?>("VoteArenaMessage", Vote(chatSessionId, battleId, "Alfa"));
        var error = await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync<string?>("VoteArenaMessage", Vote(chatSessionId, battleId, "Beta")));

        Assert.Equal("respuesta A", shown);
        Assert.Contains(HubErrors.Message(HubErrors.AlreadyVoted), error.Message);
        Assert.Equal(["respuesta A"], StoredAnswers(chatSessionId));
        var winner = await factory.QueryAsync(db => db.ArenaBattles.IgnoreQueryFilters()
            .Where(b => b.SessionId == battleId).Select(b => b.Winner).SingleAsync());
        Assert.Equal(BattleWinner.Alfa, winner);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }
    }
}
