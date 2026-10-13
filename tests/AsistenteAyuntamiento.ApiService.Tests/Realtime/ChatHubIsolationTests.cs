using System.Net;
using AsistenteAyuntamiento.ApiService.Features.Chat;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace AsistenteAyuntamiento.ApiService.Tests.Realtime;

/// <summary>
/// The chat hub through a real SignalR client (spec: signalr-integration): authentication, tenant and session
/// ownership. None of these cases reaches the model.
/// </summary>
public class ChatHubIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncDisposable
{
    private readonly List<HubConnection> _connections = [];

    private async Task<HubConnection> ConnectAsync(string? user, string? org)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/chat"), options =>
            {
                // TestServer has no real sockets: long polling over its in-memory handler.
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (user is not null) options.Headers[TestAuthHandler.UserHeader] = user;
                if (org is not null) options.Headers[TestAuthHandler.OrgHeader] = org;
            })
            .Build();
        _connections.Add(connection);
        await connection.StartAsync();
        return connection;
    }

    private static async Task<List<string>> StreamAsync(HubConnection connection, string sessionId)
    {
        var chunks = new List<string>();
        await foreach (var chunk in connection.StreamAsync<string>("StreamMessage", sessionId, "¿Qué dice mi conversación?"))
        {
            chunks.Add(chunk);
        }
        return chunks;
    }

    [Fact]
    public async Task Anonymous_connections_are_rejected()
    {
        var act = () => ConnectAsync(user: null, org: null);

        var error = await Assert.ThrowsAsync<HttpRequestException>(act);
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task Connections_without_an_organization_are_forbidden()
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync("auth0|no-org", org: null));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task Another_user_cannot_see_stream_into_load_or_delete_a_session()
    {
        var owner = await ConnectAsync("auth0|owner", "org_hub");
        var sessionId = await owner.InvokeAsync<Guid>("CreateNewSession");

        var intruder = await ConnectAsync("auth0|intruder", "org_hub");

        Assert.DoesNotContain(await intruder.InvokeAsync<List<ChatSessionSummary>>("GetSessions"), s => s.Id == sessionId);
        Assert.Empty(await intruder.InvokeAsync<List<ChatMessage>>("LoadSession", sessionId));
        Assert.Equal([HubErrors.Message(HubErrors.NotFound)], await StreamAsync(intruder, sessionId.ToString()));

        await intruder.InvokeAsync("DeleteSession", sessionId);
        Assert.Contains(await owner.InvokeAsync<List<ChatSessionSummary>>("GetSessions"), s => s.Id == sessionId);
    }

    [Fact]
    public async Task The_same_user_in_another_organization_does_not_see_the_session()
    {
        var inFirstOrg = await ConnectAsync("auth0|multi-org", "org_first");
        var sessionId = await inFirstOrg.InvokeAsync<Guid>("CreateNewSession");

        var inSecondOrg = await ConnectAsync("auth0|multi-org", "org_second");

        Assert.Empty(await inSecondOrg.InvokeAsync<List<ChatSessionSummary>>("GetSessions"));
        Assert.Equal([HubErrors.Message(HubErrors.NotFound)], await StreamAsync(inSecondOrg, sessionId.ToString()));
    }

    [Fact]
    public async Task A_malformed_session_id_gets_a_typed_error_without_details()
    {
        var connection = await ConnectAsync("auth0|owner", "org_hub");

        var chunks = await StreamAsync(connection, "not-a-guid");

        Assert.Equal([HubErrors.Message(HubErrors.InvalidRequest)], chunks);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>Shape of ChatSessionSummaryDto / ChatMessageDto as the client receives them.</summary>
    private sealed record ChatSessionSummary(Guid Id, DateTime CreatedAt, string Preview, int MessageCount);

    private sealed record ChatMessage(string Role, string Content, DateTime CreatedAt);
}
