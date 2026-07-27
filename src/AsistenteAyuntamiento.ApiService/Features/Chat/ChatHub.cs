using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AsistenteAyuntamiento.ApiService.Features.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

/// <summary>
/// SignalR hub for real-time chat. Thin entry point that delegates
/// persistence to <see cref="ChatSessionService"/> and AI calls to <see cref="AiChatService"/>.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly CurrentTenantService _tenantService;
    private readonly ChatSessionService _sessionService;
    private readonly AiChatService _aiChatService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        CurrentTenantService tenantService,
        ChatSessionService sessionService,
        AiChatService aiChatService,
        ILogger<ChatHub> logger)
    {
        _tenantService = tenantService;
        _sessionService = sessionService;
        _aiChatService = aiChatService;
        _logger = logger;
    }

    public async Task SendMessage(string message)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = _tenantService.TenantId;

        if (string.IsNullOrEmpty(userId))
        {
            await Clients.Caller.SendAsync("ReceiveMessage", "Error: No user ID found.");
            return;
        }

        try
        {
            _logger.LogInformation("Message from {User} in tenant {Tenant}", userId, tenantId);

            // 1. Session & user message persistence
            var session = await _sessionService.GetOrCreateSessionAsync(userId, tenantId);
            _sessionService.AddUserMessage(session, message);
            await _sessionService.SaveAsync();

            // 2. Build history for the model
            var recentMessages = _sessionService.GetCompactedHistory(session);
            var history = BuildChatHistory(recentMessages);

            // 3. Call AI (metrics and tracing are handled inside AiChatService)
            var result = await _aiChatService.GetCompletionAsync(history, tenantId, userId);

            // 4. Persist assistant response
            _sessionService.AddAssistantMessage(session, result.Content);
            await _sessionService.SaveAsync();

            // 5. Send to client
            await Clients.Caller.SendAsync("ReceiveMessage", result.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in SendMessage for {User}", userId);
            await Clients.Caller.SendAsync("ReceiveMessage", $"System Error: {ex.GetType().Name} - {ex.Message}");
        }
    }

    /// <summary>
    /// Converts persisted <see cref="ChatMessage"/> entities into a Semantic Kernel <see cref="ChatHistory"/>.
    /// </summary>
    private static ChatHistory BuildChatHistory(List<ChatMessage> messages)
    {
        var history = new ChatHistory(
            "Eres un asistente virtual del ayuntamiento. Responde de forma clara y concisa en español.");

        foreach (var msg in messages)
        {
            if (msg.Role == "user")
                history.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant")
                history.AddAssistantMessage(msg.Content);
        }

        return history;
    }
}
