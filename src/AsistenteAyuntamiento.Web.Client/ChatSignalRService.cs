using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AsistenteAyuntamiento.Web.Client;

public class ChatSignalRService : IAsyncDisposable
{
    private readonly ChatHubOptions _hubOptions;
    private readonly AppTokenProvider _tokenProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider _authStateProvider;
    private HubConnection? _hubConnection;

    public event Action<string>? OnMessageReceived;

    public ChatSignalRService(
        IOptions<ChatHubOptions> hubOptions,
        AppTokenProvider tokenProvider,
        IServiceProvider serviceProvider,
        Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authStateProvider)
    {
        _hubOptions = hubOptions.Value;
        _tokenProvider = tokenProvider;
        _serviceProvider = serviceProvider;
        _authStateProvider = authStateProvider;
    }

    public async Task ConnectAsync()
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected) return;

        _hubConnection ??= new HubConnectionBuilder()
            .WithUrl(_hubOptions.HubUrl, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    if (!string.IsNullOrEmpty(_tokenProvider.AccessToken))
                    {
                        return _tokenProvider.AccessToken;
                    }
                    var authState = await _authStateProvider.GetAuthenticationStateAsync();
                    return authState.User.FindFirst("access_token")?.Value ?? string.Empty;
                };

                // On the server, use the handler from IHttpClientFactory which has
                // Aspire service discovery configured — this resolves "http://apiservice"
                // to the actual host:port.
                var handlerFactory = _serviceProvider.GetService<IHttpMessageHandlerFactory>();
                if (handlerFactory is not null)
                {
                    options.HttpMessageHandlerFactory = _ => handlerFactory.CreateHandler();
                }
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<string>("ReceiveMessage", (message) =>
        {
            OnMessageReceived?.Invoke(message);
        });

        await _hubConnection.StartAsync();
    }

    public async Task SendMessageAsync(Guid sessionId, string message)
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
        {
            await _hubConnection.SendAsync("SendMessage", sessionId, message);
        }
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(Guid sessionId, string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
        {
            var stream = _hubConnection.StreamAsync<string>("StreamMessage", sessionId, message, cancellationToken);
            await foreach (var chunk in stream)
            {
                yield return chunk;
            }
        }
    }

    public async Task<List<ChatSessionSummaryDto>> GetSessionsAsync()
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
        {
            return await _hubConnection.InvokeAsync<List<ChatSessionSummaryDto>>("GetSessions");
        }
        return new();
    }

    public async Task<List<ChatMessageDto>> LoadSessionAsync(Guid sessionId)
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
        {
            return await _hubConnection.InvokeAsync<List<ChatMessageDto>>("LoadSession", sessionId);
        }
        return new();
    }

    public async Task<Guid> CreateNewSessionAsync()
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
        {
            return await _hubConnection.InvokeAsync<Guid>("CreateNewSession");
        }
        return Guid.Empty;
    }

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}

public record ChatSessionSummaryDto(Guid Id, DateTime CreatedAt, string Preview, int MessageCount);
public record ChatMessageDto(string Role, string Content, DateTime CreatedAt);
