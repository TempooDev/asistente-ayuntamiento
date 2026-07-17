using System.Security.Claims;
using AsistenteAyuntamiento.ApiService.Features.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

[Authorize]
public class ChatHub : Hub
{
    private readonly CurrentTenantService _tenantService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(CurrentTenantService tenantService, ILogger<ChatHub> logger)
    {
        _tenantService = tenantService;
        _logger = logger;
    }

    public async Task SendMessage(string message)
    {
        var auth0Id = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = _tenantService.TenantId;

        _logger.LogInformation("Received message from {User} in Tenant {Tenant}: {Message}", auth0Id, tenantId, message);

        // TODO: Pass message to Semantic Kernel Agent here.
        // For now, just echo back.
        await Clients.Caller.SendAsync("ReceiveMessage", $"Assistant (Tenant {tenantId}): You said: {message}");
    }
}
