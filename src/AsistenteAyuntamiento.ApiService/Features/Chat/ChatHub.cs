using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using System.Security.Claims;
using AsistenteAyuntamiento.ApiService.Features.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel.ChatCompletion;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.DTOs;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

/// <summary>
/// SignalR hub for real-time chat. Thin entry point that delegates
/// persistence to <see cref="ChatSessionService"/> and AI calls to <see cref="AiChatService"/>.
/// </summary>
[Authorize]
public class ChatHub(
    CurrentTenantService tenantService,
    IChatSessionService sessionService,
    IAiChatService aiChatService,
    ILogger<ChatHub> logger) : Hub
{
    private readonly CurrentTenantService _tenantService = tenantService;
    private readonly IChatSessionService _sessionService = sessionService;
    private readonly IAiChatService _aiChatService = aiChatService;
    private readonly ILogger<ChatHub> _logger = logger;

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
                await Clients.Caller.SendAsync("ReceiveMessage", "Error: Session not found or unauthorized.");
                return;
            }
            _sessionService.EnqueueUserMessage(session, message);

            // 2. Build history for the model
            var recentMessages = _sessionService.GetCompactedHistory(session);
            var history = BuildChatHistory(recentMessages);

            // 3. Call AI (metrics and tracing are handled inside AiChatService)
            var result = await _aiChatService.GetCompletionAsync(history, tenantId, userId);

            var finalContent = result.Content;


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

    public async IAsyncEnumerable<string> StreamMessage(
        string sessionIdStr,
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = _tenantService.TenantId;

        if (string.IsNullOrEmpty(userId))
        {
            yield return "Error: No user ID found.";
            yield break;
        }

        ChatSession? session = null;
        string? errorMessage = null;

        try
        {
            _logger.LogInformation("Streaming message from {User} in tenant {Tenant} for session {SessionId}", userId, tenantId, sessionIdStr);

            if (!Guid.TryParse(sessionIdStr, out var sessionId))
            {
                errorMessage = "Error: Invalid session ID format.";
            }
            else
            {
                session = await _sessionService.GetSessionByIdAsync(sessionId, userId, tenantId);
                if (session == null)
                {
                    errorMessage = "Error: Session not found or unauthorized.";
                }
                else
                {
                    _sessionService.EnqueueUserMessage(session, message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error preparing stream for {User}", userId);
            errorMessage = $"System Error: {ex.GetType().Name} - {ex.Message}";
        }

        if (errorMessage != null || session == null)
        {
            yield return errorMessage ?? "Unexpected error.";
            yield break;
        }

        var recentMessages = _sessionService.GetCompactedHistory(session);
        var history = BuildChatHistory(recentMessages);

        var fullResponseBuilder = new System.Text.StringBuilder();

        await foreach (var chunk in _aiChatService.GetStreamingCompletionAsync(history, tenantId, userId, cancellationToken))
        {
            fullResponseBuilder.Append(chunk);
            yield return chunk;
        }

        _sessionService.EnqueueAssistantMessage(session, fullResponseBuilder.ToString());
    }

    public async IAsyncEnumerable<ArenaStreamChunk> StreamArenaMessage(
        string sessionIdStr,
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = _tenantService.TenantId;

        if (string.IsNullOrEmpty(userId))
        {
            yield return new ArenaStreamChunk("Error", "No user ID found.");
            yield break;
        }

        ChatSession? session = null;
        string? errorMessage = null;

        try
        {
            _logger.LogInformation("Streaming ARENA message from {User} in tenant {Tenant} for session {SessionId}", userId, tenantId, sessionIdStr);

            if (!Guid.TryParse(sessionIdStr, out var sessionId))
            {
                errorMessage = "Invalid session ID format.";
            }
            else
            {
                session = await _sessionService.GetSessionByIdAsync(sessionId, userId, tenantId);
                if (session == null)
                {
                    errorMessage = "Session not found or unauthorized.";
                }
                else
                {
                    _sessionService.EnqueueUserMessage(session, message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error preparing ARENA stream for {User}", userId);
            errorMessage = $"System Error: {ex.Message}";
        }

        if (errorMessage != null || session == null)
        {
            yield return new ArenaStreamChunk("Error", errorMessage ?? "Unexpected error.");
            yield break;
        }

        var recentMessages = _sessionService.GetCompactedHistory(session);
        var history = BuildChatHistory(recentMessages);

        await foreach (var chunk in _aiChatService.GetArenaStreamingCompletionAsync(history, tenantId, userId, cancellationToken))
        {
            yield return chunk;
        }

        // We do NOT enqueue assistant message here because we wait for the vote!
        // The winning message will be saved by the VoteArenaMessage endpoint or SignalR method.
    }

    public async Task VoteArenaMessage(ArenaChatVoteRequest request)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = _tenantService.TenantId;

        if (string.IsNullOrEmpty(userId)) return;

        using var scope = Context.GetHttpContext()!.RequestServices.CreateScope();
        var arenaService = scope.ServiceProvider.GetRequiredService<AsistenteAyuntamiento.Application.Features.Arena.IArenaService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AsistenteAyuntamiento.Application.Common.Interfaces.IAppDbContext>();
        
        var voteResult = await arenaService.VoteAsync(new AsistenteAyuntamiento.Application.Features.Arena.Models.ArenaVoteRequest 
        { 
            SessionId = request.BattleId, 
            Winner = request.Winner 
        });

        var session = await _sessionService.GetSessionByIdAsync(request.ChatSessionId, userId, tenantId);
        if (session != null)
        {
            var battle = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                dbContext.ArenaBattles, b => b.SessionId == request.BattleId);

            if (battle != null)
            {
                var winnerText = request.Winner.Equals("Alfa", StringComparison.OrdinalIgnoreCase) 
                    ? battle.LeftResponse 
                    : (request.Winner.Equals("Beta", StringComparison.OrdinalIgnoreCase) ? battle.RightResponse : null);

                if (winnerText != null)
                {
                    _sessionService.EnqueueAssistantMessage(session, winnerText);
                }
            }
        }
    }

    /// <summary>
    /// Converts persisted <see cref="ChatMessage"/> entities into a Semantic Kernel <see cref="ChatHistory"/>.
    /// </summary>
    private static ChatHistory BuildChatHistory(List<ChatMessage> messages)
    {
        var history = new ChatHistory(
            "You are a virtual assistant for the city council. Respond clearly and concisely in Spanish.");

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
        return sessions.Select(s =>
        {
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

        var session = await _sessionService.CreateNewSessionAsync(userId, tenantId);
        return session.Id;
    }

    public async Task DeleteSession(Guid sessionId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = _tenantService.TenantId;

        if (string.IsNullOrEmpty(userId))
            throw new HubException("User ID not found");

        await _sessionService.DeleteSessionAsync(sessionId, userId, tenantId);
    }
}






