using System.Security.Claims;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Arena.Models;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.DTOs;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

/// <summary>
/// SignalR hub for real-time chat. Thin entry point that delegates
/// persistence to <see cref="ChatSessionService"/> and AI calls to <see cref="AiChatService"/>.
/// </summary>
[Authorize]
public class ChatHub(
    ICurrentTenantService tenantService,
    IChatSessionService sessionService,
    IAiChatService aiChatService,
    IArenaService arenaService,
    ILogger<ChatHub> logger) : Hub
{
    private readonly ICurrentTenantService _tenantService = tenantService;
    private readonly IChatSessionService _sessionService = sessionService;
    private readonly IAiChatService _aiChatService = aiChatService;
    private readonly IArenaService _arenaService = arenaService;
    private readonly ILogger<ChatHub> _logger = logger;

    private string? CurrentUserId => Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// TenantRequiredMiddleware rejects connections without a tenant, so this is only null
    /// if that guarantee is broken; fail closed instead of using a shared tenant.
    /// </summary>
    private string CurrentTenantId => _tenantService.TenantId
        ?? throw new HubException(HubErrors.Message(HubErrors.Unauthorized));

    public async Task SendMessage(Guid sessionId, string message)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
        {
            await Clients.Caller.SendAsync("ReceiveMessage", HubErrors.Message(HubErrors.Unauthorized));
            return;
        }

        try
        {
            var tenantId = CurrentTenantId;
            _logger.LogInformation("Message from {User} in tenant {Tenant} for session {SessionId}", userId, tenantId, sessionId);

            // 1. Fetch specific session
            var session = await _sessionService.GetSessionByIdAsync(sessionId, userId, tenantId, Context.ConnectionAborted);
            if (session == null)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", HubErrors.Message(HubErrors.NotFound));
                return;
            }
            _sessionService.EnqueueUserMessage(session, message);

            // 2. Build history for the model
            var recentMessages = _sessionService.GetCompactedHistory(session);
            var history = BuildChatHistory(recentMessages);

            // 3. Call AI (metrics and tracing are handled inside AiChatService)
            var result = await _aiChatService.GetCompletionAsync(history, tenantId, userId, Context.ConnectionAborted);

            var finalContent = result.Content;

            // 4. Persist assistant response
            _sessionService.EnqueueAssistantMessage(session, finalContent);

            // 5. Send to client
            await Clients.Caller.SendAsync("ReceiveMessage", finalContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in SendMessage for {User}", userId);
            await Clients.Caller.SendAsync("ReceiveMessage", HubErrors.Message(HubErrors.Internal));
        }
    }

    public async IAsyncEnumerable<string> StreamMessage(
        string sessionIdStr,
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
        {
            yield return HubErrors.Message(HubErrors.Unauthorized);
            yield break;
        }

        var tenantId = CurrentTenantId;
        ChatSession? session = null;
        string? errorMessage = null;

        try
        {
            _logger.LogInformation("Streaming message from {User} in tenant {Tenant} for session {SessionId}", userId, tenantId, sessionIdStr);

            if (!Guid.TryParse(sessionIdStr, out var sessionId))
            {
                errorMessage = HubErrors.Message(HubErrors.InvalidRequest);
            }
            else
            {
                session = await _sessionService.GetSessionByIdAsync(sessionId, userId, tenantId, cancellationToken);
                if (session == null)
                {
                    errorMessage = HubErrors.Message(HubErrors.NotFound);
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
            errorMessage = HubErrors.Message(HubErrors.Internal);
        }

        if (errorMessage != null || session == null)
        {
            yield return errorMessage ?? HubErrors.Message(HubErrors.Internal);
            yield break;
        }

        var recentMessages = _sessionService.GetCompactedHistory(session);
        var history = BuildChatHistory(recentMessages);

        var fullResponseBuilder = new System.Text.StringBuilder();

        var stream = Guarded(
            _aiChatService.GetStreamingCompletionAsync(history, tenantId, userId, cancellationToken),
            cancellationToken,
            llmErrorCode => SaveInterruptedAnswer(session, fullResponseBuilder.ToString(), llmErrorCode));
        await foreach (var chunk in stream)
        {
            fullResponseBuilder.Append(chunk);
            yield return chunk;
        }

        // A stream the client cancelled (it left or deleted the conversation) stores no answer.
        if (!cancellationToken.IsCancellationRequested)
        {
            _sessionService.EnqueueAssistantMessage(session, fullResponseBuilder.ToString());
        }
    }

    /// <summary>
    /// Stores the text streamed before a failure followed by the notice the client appends for it
    /// (<see cref="HubErrors.StreamFailureNotice"/>), so a reload shows what the user saw. Nothing is stored when no
    /// text was streamed.
    /// </summary>
    private void SaveInterruptedAnswer(ChatSession session, string partialAnswer, string? llmErrorCode)
    {
        if (string.IsNullOrWhiteSpace(partialAnswer))
        {
            return;
        }

        _sessionService.EnqueueAssistantMessage(session, partialAnswer + HubErrors.StreamFailureNotice(llmErrorCode));
    }

    public async IAsyncEnumerable<ArenaStreamChunk> StreamArenaMessage(
        string sessionIdStr,
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
        {
            yield return new ArenaStreamChunk("Error", HubErrors.Message(HubErrors.Unauthorized));
            yield break;
        }

        var tenantId = CurrentTenantId;
        ChatSession? session = null;
        string? errorMessage = null;

        try
        {
            _logger.LogInformation("Streaming ARENA message from {User} in tenant {Tenant} for session {SessionId}", userId, tenantId, sessionIdStr);

            if (!Guid.TryParse(sessionIdStr, out var sessionId))
            {
                errorMessage = HubErrors.Message(HubErrors.InvalidRequest);
            }
            else
            {
                session = await _sessionService.GetSessionByIdAsync(sessionId, userId, tenantId, cancellationToken);
                if (session == null)
                {
                    errorMessage = HubErrors.Message(HubErrors.NotFound);
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
            errorMessage = HubErrors.Message(HubErrors.Internal);
        }

        if (errorMessage != null || session == null)
        {
            // A thrown HubException reaches the client as a stream error, shown in both Arena columns.
            throw new HubException(errorMessage ?? HubErrors.Message(HubErrors.Internal));
        }

        var recentMessages = _sessionService.GetCompactedHistory(session);
        var history = BuildChatHistory(recentMessages);

        await foreach (var chunk in Guarded(_aiChatService.GetArenaStreamingCompletionAsync(history, tenantId, userId, cancellationToken), cancellationToken))
        {
            yield return chunk;
        }

        // We do NOT enqueue assistant message here because we wait for the vote!
        // The winning message will be saved by the VoteArenaMessage endpoint or SignalR method.
    }

    /// <summary>
    /// Records a chat Arena vote and stores the answer the chat keeps (the chosen side, or both for a tie). Returns
    /// that answer so the client shows exactly what a reload will show.
    /// </summary>
    public async Task<string?> VoteArenaMessage(ArenaChatVoteRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
            throw new HubException(HubErrors.Message(HubErrors.Unauthorized));

        var tenantId = CurrentTenantId;

        ArenaVoteResponse? voteResult;
        try
        {
            voteResult = await _arenaService.VoteAsync(new ArenaVoteRequest
            {
                SessionId = request.BattleId,
                Winner = request.Winner
            }, userId);
        }
        catch (ArgumentException)
        {
            throw new HubException(HubErrors.Message(HubErrors.InvalidRequest));
        }
        catch (ArenaVoteConflictException)
        {
            // Votes are final: nothing changes and no second answer is added to the chat.
            throw new HubException(HubErrors.Message(HubErrors.AlreadyVoted));
        }

        // Unknown battle or owned by someone else: same answer, so existence is not revealed.
        if (voteResult is null)
            throw new HubException(HubErrors.Message(HubErrors.NotFound));

        // The chosen answer (both for a tie) becomes the assistant message of the chat; "both bad" adds nothing.
        if (voteResult.ChatAnswer is not null)
        {
            var session = await _sessionService.GetSessionByIdAsync(request.ChatSessionId, userId, tenantId, Context.ConnectionAborted);
            if (session != null)
            {
                _sessionService.EnqueueAssistantMessage(session, voteResult.ChatAnswer);
            }
        }

        return voteResult.ChatAnswer;
    }

    /// <summary>
    /// SignalR enumerates a stream after the hub method has returned, outside any try/catch in it. This turns an
    /// unexpected failure in the middle of a stream into the typed <c>Error [INTERNAL]</c> (details only in the logs),
    /// and a model failure after some text into its <c>[LLM_*]</c> code; the client maps them like any other hub error.
    /// <paramref name="onFailure"/> runs before the error is thrown (with the <c>LLM_*</c> code, or null for an internal
    /// error), never for a stream the client cancelled.
    /// </summary>
    private async IAsyncEnumerable<T> Guarded<T>(
        IAsyncEnumerable<T> source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        Action<string?>? onFailure = null)
    {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (LlmStreamInterruptedException ex)
            {
                // The model failed after part of the answer was sent: the client keeps it and shows the typed code.
                _logger.LogWarning("Chat stream interrupted ({ErrorCode}) for {User}", ex.ErrorCode, CurrentUserId);
                if (!cancellationToken.IsCancellationRequested) onFailure?.Invoke(ex.ErrorCode);
                throw new HubException(ex.Message);
            }
            catch (Exception ex) when (ex is not HubException && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                _logger.LogError(ex, "Chat stream failed for {User}", CurrentUserId);
                if (!cancellationToken.IsCancellationRequested) onFailure?.Invoke(null);
                throw new HubException(HubErrors.Message(HubErrors.Internal));
            }

            if (!hasNext) yield break;
            yield return enumerator.Current;
        }
    }

    /// <summary>
    /// Converts persisted <see cref="ChatMessage"/> entities into a Semantic Kernel <see cref="ChatHistory"/>. No system
    /// message: the pipeline adds its own (RAG instructions and the user's preferences), and only does so when the
    /// history has none.
    /// </summary>
    private static ChatHistory BuildChatHistory(List<ChatMessage> messages)
    {
        var history = new ChatHistory();

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
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
            return new List<ChatSessionSummaryDto>();

        var sessions = await _sessionService.GetUserSessionsAsync(userId, CurrentTenantId, Context.ConnectionAborted);
        return sessions.Select(s =>
        {
            var firstUserMsg = s.Messages.OrderBy(m => m.CreatedAt).FirstOrDefault(m => m.Role == "user")?.Content ?? "";
            var preview = firstUserMsg.Length > 80 ? firstUserMsg.Substring(0, 80) + "..." : firstUserMsg;
            return new ChatSessionSummaryDto(s.Id, s.CreatedAt, preview, s.Messages.Count);
        }).ToList();
    }

    public async Task<List<ChatMessageDto>> LoadSession(Guid sessionId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
            return new List<ChatMessageDto>();

        var messages = await _sessionService.GetSessionMessagesAsync(sessionId, userId, CurrentTenantId, Context.ConnectionAborted);
        return messages.Select(m => new ChatMessageDto(m.Role, m.Content, m.CreatedAt)).ToList();
    }

    public async Task<Guid> CreateNewSession()
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
            throw new HubException(HubErrors.Message(HubErrors.Unauthorized));

        var session = await _sessionService.CreateNewSessionAsync(userId, CurrentTenantId, Context.ConnectionAborted);
        return session.Id;
    }

    public async Task DeleteSession(Guid sessionId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
            throw new HubException(HubErrors.Message(HubErrors.Unauthorized));

        await _sessionService.DeleteSessionAsync(sessionId, userId, CurrentTenantId, Context.ConnectionAborted);
    }
}
