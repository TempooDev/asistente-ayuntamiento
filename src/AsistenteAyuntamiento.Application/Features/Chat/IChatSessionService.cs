using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;

namespace AsistenteAyuntamiento.Application.Features.Chat;

public interface IChatSessionService
{
    Task<ChatSession> CreateNewSessionAsync(string userId, string tenantId);
    ChatMessage AddUserMessage(ChatSession session, string content);
    ChatMessage AddAssistantMessage(ChatSession session, string content);
    List<ChatMessage> GetCompactedHistory(ChatSession session, int maxMessages = 20);
    Task SaveAsync();
    void EnqueueUserMessage(ChatSession session, string content);
    void EnqueueAssistantMessage(ChatSession session, string content);
    Task<List<ChatSession>> GetUserSessionsAsync(string userId, string tenantId);
    Task<List<ChatMessage>> GetSessionMessagesAsync(Guid sessionId, string userId, string tenantId);
    Task<bool> DeleteSessionAsync(Guid sessionId, string userId, string tenantId);
    Task<ChatSession?> GetSessionByIdAsync(Guid sessionId, string userId, string tenantId);
}
