using Microsoft.AspNetCore.SignalR.Client;

namespace AsistenteAyuntamiento.Web.Client;

public class ChatSignalRService : IAsyncDisposable
{
    private readonly HubConnection _hubConnection;

    public event Action<string>? OnMessageReceived;

    public ChatSignalRService(HubConnection hubConnection)
    {
        _hubConnection = hubConnection;
    }

    public async Task ConnectAsync()
    {
        if (_hubConnection.State == HubConnectionState.Connected) return;

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
