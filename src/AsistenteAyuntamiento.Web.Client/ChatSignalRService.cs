using Microsoft.AspNetCore.SignalR.Client;

namespace AsistenteAyuntamiento.Web.Client;

public class ChatSignalRService : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private readonly AppTokenProvider _tokenProvider;
    private readonly IConfiguration _configuration;

    public event Action<string>? OnMessageReceived;

    public ChatSignalRService(AppTokenProvider tokenProvider, IConfiguration configuration)
    {
        _tokenProvider = tokenProvider;
        _configuration = configuration;
    }

    public async Task ConnectAsync()
    {
        if (_hubConnection is not null) return;

        var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "https://localhost:7573";
        var hubUrl = $"{apiBaseUrl.TrimEnd('/')}/hubs/chat";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(_tokenProvider.AccessToken);
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<string>("ReceiveMessage", (message) =>
        {
            OnMessageReceived?.Invoke(message);
        });

        await _hubConnection.StartAsync();
    }

    public async Task SendMessageAsync(string message)
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
        {
            await _hubConnection.SendAsync("SendMessage", message);
        }
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
