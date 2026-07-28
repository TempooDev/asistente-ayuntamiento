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

    public async Task SendMessage(Guid sessionId, string message)
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
            _logger.LogInformation("Message from {User} in tenant {Tenant} for session {SessionId}", userId, tenantId, sessionId);

            // 1. Fetch specific session
            var session = await _sessionService.GetSessionByIdAsync(sessionId, userId, tenantId);
            if (session == null)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "Error: Sesión no encontrada o no autorizada.");
                return;
            }
            _sessionService.EnqueueUserMessage(session, message);

            // 2. Build history for the model
            var recentMessages = _sessionService.GetCompactedHistory(session);
            var history = BuildChatHistory(recentMessages);

            // 3. Call AI (metrics and tracing are handled inside AiChatService)
            var result = await _aiChatService.GetCompletionAsync(history, tenantId, userId);

            var finalContent = result.Content;
            if (result.Sources?.Any() == true)
            {
                finalContent += "\n\n**Fuentes consultadas:**\n";
                foreach (var src in result.Sources)
                {
                    finalContent += $"- [{src.Title}]({src.BlobPath}) - {src.Department} ({src.Date})\n";
                }
            }

            // 4. Persist assistant response
            _sessionService.EnqueueAssistantMessage(session, finalContent);

            // 5. Send to client
            await Clients.Caller.SendAsync("ReceiveMessage", finalContent);
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

    public async Task<List<ChatSessionSummaryDto>> GetSessions()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = _tenantService.TenantId;

        if (string.IsNullOrEmpty(userId))
            return new List<ChatSessionSummaryDto>();

        var sessions = await _sessionService.GetUserSessionsAsync(userId, tenantId);
        return sessions.Select(s => {
            var firstUserMsg = s.Messages.OrderBy(m => m.CreatedAt).FirstOrDefault(m => m.Role == "user")?.Content ?? "";
            var preview = firstUserMsg.Length > 80 ? firstUserMsg.Substring(0, 80) + "..." : firstUserMsg;
            return new ChatSessionSummaryDto(s.Id, s.CreatedAt, preview, s.Messages.Count);
        }).ToList();
    }

    public async Task<List<ChatMessageDto>> LoadSession(Guid sessionId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = _tenantService.TenantId;

        if (string.IsNullOrEmpty(userId))
            return new List<ChatMessageDto>();

        var messages = await _sessionService.GetSessionMessagesAsync(sessionId, userId, tenantId);
        return messages.Select(m => new ChatMessageDto(m.Role, m.Content, m.CreatedAt)).ToList();
    }

    public async Task<Guid> CreateNewSession()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = _tenantService.TenantId;

        if (string.IsNullOrEmpty(userId))
            throw new HubException("User ID not found");

        // Force creation of a brand new session
        var session = await _sessionService.CreateNewSessionAsync(userId, tenantId);
        return session.Id;
    }
}
