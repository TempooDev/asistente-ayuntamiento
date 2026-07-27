namespace AsistenteAyuntamiento.Web.Client;

/// <summary>
/// Configuration for the SignalR chat hub connection.
/// Each host (server / WASM) sets the appropriate HubUrl.
/// </summary>
public class ChatHubOptions
{
    /// <summary>
    /// Absolute URL to the SignalR chat hub endpoint.
    /// Server: "http://apiservice/hubs/chat" (direct, bypassing gateway).
    /// WASM:   derived from NavigationManager (browser origin + "/hubs/chat").
    /// </summary>
    public string HubUrl { get; set; } = string.Empty;
}
